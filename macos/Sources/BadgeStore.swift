import Foundation
import CryptoKit
import ImageIO

enum BadgeSource {
    static func allowed(_ url: URL) -> Bool {
        url.scheme == "https" && url.host == "a.espncdn.com" && url.user == nil && url.password == nil && (url.port == nil || url.port == 443)
    }
    static func urls(team: Team?, catalog: [String] = []) -> [URL] {
        guard let team else { return [] }
        var candidates = [team.logo] + catalog
        if !team.id.isEmpty && team.id.allSatisfy({ $0.isASCII && $0.isNumber }) {
            candidates.append("https://a.espncdn.com/i/teamlogos/soccer/500/\(team.id).png")
        }
        var seen = Set<URL>()
        return candidates.compactMap(URL.init(string:)).filter { allowed($0) && seen.insert($0).inserted }
    }
}

private actor BadgeSlots {
    private var available = 6
    private var waiting: [CheckedContinuation<Void, Never>] = []
    func acquire() async {
        if available > 0 { available -= 1; return }
        await withCheckedContinuation { waiting.append($0) }
    }
    func release() {
        if waiting.isEmpty { available += 1 }
        else { waiting.removeFirst().resume() }
    }
}

/// One shared store for match rows, favorites, the compact card and the menu bar.
/// A view's cancellation never cancels a download another view is waiting for.
actor BadgeStore {
    static let lifetime: TimeInterval = 30 * 24 * 3600
    private struct Entry { let data: Data; let updated: Date }
    private let directory: URL
    private let session: URLSession
    private let retryDelay: UInt64
    private let failureCooldown: TimeInterval
    private let slots = BadgeSlots()
    private var memory: [URL: Entry] = [:]
    private var pending: [URL: Task<Data?, Never>] = [:]
    private var retryAfter: [URL: Date] = [:]
    private static let maximumBytes = 2 * 1024 * 1024
    static let defaultSession: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 10
        config.timeoutIntervalForResource = 15
        config.httpMaximumConnectionsPerHost = 6
        config.httpAdditionalHeaders = ["User-Agent": "TouchlineDesktop-macOS/1.3.1"]
        return URLSession(configuration: config)
    }()

    init(directory: URL, session: URLSession = BadgeStore.defaultSession, retryDelay: UInt64 = 1_500_000_000, failureCooldown: TimeInterval = 60) {
        self.directory = directory
        self.session = session
        self.retryDelay = retryDelay
        self.failureCooldown = failureCooldown
    }
    func file(for url: URL) -> URL {
        let key = SHA256.hash(data: Data(url.absoluteString.utf8)).map { String(format: "%02x", $0) }.joined()
        return directory.appendingPathComponent(key + ".png")
    }
    private func entry(for url: URL) -> Entry? {
        if let entry = memory[url] { return entry }
        let path = file(for: url)
        guard let values = try? path.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey]),
              (values.fileSize ?? 0) <= Self.maximumBytes,
              let updated = values.contentModificationDate,
              let data = try? Data(contentsOf: path), Self.normalizedPNG(data) != nil else { return nil }
        let value = Entry(data: data, updated: updated)
        remember(value, for: url)
        return value
    }
    private func remember(_ entry: Entry, for url: URL) {
        // Bounded memory; disk remains the persistent source after eviction.
        if memory.count >= 256, memory[url] == nil, let key = memory.keys.first { memory.removeValue(forKey: key) }
        memory[url] = entry
    }
    /// Show a usable local image immediately, even while an expired one refreshes.
    func cached(_ urls: [URL]) -> Data? {
        var fallback: Data?
        for url in urls where BadgeSource.allowed(url) {
            if let entry = entry(for: url) {
                if Date().timeIntervalSince(entry.updated) < Self.lifetime { return entry.data }
                if fallback == nil { fallback = entry.data }
            }
        }
        return fallback
    }
    func load(_ urls: [URL]) async -> Data? {
        var seen = Set<URL>()
        let urls = urls.filter { BadgeSource.allowed($0) && seen.insert($0).inserted }
        var fallback: Data?
        for url in urls {
            if let entry = entry(for: url) {
                if Date().timeIntervalSince(entry.updated) < Self.lifetime { return entry.data }
                if fallback == nil { fallback = entry.data }
            }
        }
        for url in urls {
            if let data = await refresh(url) { return data }
        }
        return fallback
    }
    func clearFailureCooldown() { retryAfter.removeAll() }
    private func refresh(_ url: URL) async -> Data? {
        if let task = pending[url] { return await task.value }
        if let next = retryAfter[url], next > Date() { return nil }
        let task = Task { [session, slots, retryDelay] () -> Data? in
            for attempt in 0..<2 {
                if attempt > 0 { try? await Task.sleep(nanoseconds: retryDelay) }
                await slots.acquire()
                let data = await Self.download(url, session: session)
                await slots.release()
                if let data { return data }
            }
            return nil
        }
        pending[url] = task
        let data = await task.value
        pending.removeValue(forKey: url)
        if let data {
            remember(Entry(data: data, updated: Date()), for: url)
            retryAfter.removeValue(forKey: url)
            try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            try? data.write(to: file(for: url), options: .atomic)
        } else {
            retryAfter[url] = Date().addingTimeInterval(failureCooldown)
        }
        return data
    }
    private static func download(_ url: URL, session: URLSession) async -> Data? {
        do {
            let request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 10)
            let (bytes, response) = try await session.bytes(for: request)
            defer { bytes.task.cancel() }
            guard let response = response as? HTTPURLResponse, (200..<300).contains(response.statusCode),
                  let finalURL = response.url, BadgeSource.allowed(finalURL), response.expectedContentLength <= Int64(maximumBytes) else { return nil }
            var data = Data()
            for try await byte in bytes {
                guard data.count < maximumBytes else { return nil }
                data.append(byte)
            }
            return normalizedPNG(data)
        } catch { return nil }
    }
    /// Decode before saving, preserve transparency, and cap decoded images at 128 px.
    static func normalizedPNG(_ data: Data) -> Data? {
        guard !data.isEmpty, data.count <= maximumBytes,
              let source = CGImageSourceCreateWithData(data as CFData, nil),
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let width = properties[kCGImagePropertyPixelWidth] as? Int,
              let height = properties[kCGImagePropertyPixelHeight] as? Int,
              width > 0, height > 0, width <= 4096, height <= 4096,
              let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [kCGImageSourceCreateThumbnailFromImageAlways: true,
                  kCGImageSourceThumbnailMaxPixelSize: 128, kCGImageSourceCreateThumbnailWithTransform: true] as CFDictionary) else { return nil }
        let result = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(result, "public.png" as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(destination, image, nil)
        guard CGImageDestinationFinalize(destination) else { return nil }
        return result as Data
    }
}
