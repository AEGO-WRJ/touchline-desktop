import Foundation
import CoreGraphics
import ImageIO

private final class BadgeProtocol: URLProtocol {
    enum Mode { case success, offline, firstFails, invalidImage, oversized, primaryFails }
    private static let lock = NSLock()
    private static var mode: Mode = .success
    private static var image = Data()
    private static var requests = 0
    private static var active = 0
    private static var maximumActive = 0
    static func reset(_ value: Mode, image: Data) {
        lock.lock(); defer { lock.unlock() }
        mode = value; self.image = image; requests = 0; active = 0; maximumActive = 0
    }
    static var counts: (requests: Int, maximumActive: Int) {
        lock.lock(); defer { lock.unlock() }
        return (requests, maximumActive)
    }
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        Self.lock.lock()
        Self.requests += 1
        Self.active += 1
        Self.maximumActive = max(Self.maximumActive, Self.active)
        let count = Self.requests, mode = Self.mode, image = Self.image
        Self.lock.unlock()
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.04) {
            let fail = mode == .offline || (mode == .firstFails && count == 1) || (mode == .primaryFails && self.request.url!.path.contains("primary"))
            if fail {
                self.client?.urlProtocol(self, didFailWithError: URLError(.notConnectedToInternet))
            } else {
                let body = mode == .invalidImage ? Data("not an image".utf8) : image
                let size = mode == .oversized ? 3 * 1024 * 1024 : body.count
                let response = HTTPURLResponse(url: self.request.url!, statusCode: 200, httpVersion: nil, headerFields: ["Content-Length": String(size), "Content-Type": "image/png"])!
                self.client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
                self.client?.urlProtocol(self, didLoad: body)
                self.client?.urlProtocolDidFinishLoading(self)
            }
            Self.lock.lock(); Self.active -= 1; Self.lock.unlock()
        }
    }
    override func stopLoading() {}
}

private func badgeFixture() -> Data {
    let context = CGContext(data: nil, width: 500, height: 500, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    context.setFillColor(CGColor(red: 0.2, green: 0.8, blue: 0.4, alpha: 1))
    context.fill(CGRect(x: 20, y: 20, width: 460, height: 460))
    let data = NSMutableData()
    let destination = CGImageDestinationCreateWithData(data, "public.png" as CFString, 1, nil)!
    CGImageDestinationAddImage(destination, context.makeImage()!, nil)
    CGImageDestinationFinalize(destination)
    return data as Data
}

func runBadgeTests() async throws {
    let fixture = badgeFixture()
    let root = FileManager.default.temporaryDirectory.appendingPathComponent("touchline-badge-tests-\(UUID().uuidString)")
    defer { try? FileManager.default.removeItem(at: root) }
    let config = URLSessionConfiguration.ephemeral
    config.protocolClasses = [BadgeProtocol.self]
    let session = URLSession(configuration: config)
    defer { session.invalidateAndCancel() }
    func store(_ folder: String, cooldown: TimeInterval = 60) -> BadgeStore {
        BadgeStore(directory: root.appendingPathComponent(folder), session: session, retryDelay: 0, failureCooldown: cooldown)
    }
    let url = URL(string: "https://a.espncdn.com/i/teamlogos/soccer/500/359.png")!
    let bad = URL(string: "http://a.espncdn.com/i/teamlogos/soccer/500/359.png")!
    let team = Team(id: "359", name: "Arsenal", league: "eng.1", logo: "https://a.espncdn.com/primary.png")
    let urls = BadgeSource.urls(team: team, catalog: [url.absoluteString, url.absoluteString, "https://example.com/image.png"])
    check(urls == [URL(string: team.logo)!, url], "Badge sources prefer feed then catalog with deduplicated ID fallback")
    check(!BadgeSource.allowed(bad) && !BadgeSource.allowed(URL(string: "https://a.espncdn.com.evil.example/x.png")!), "Only the HTTPS ESPN image host is accepted")
    let missingLogo = BadgeSource.urls(team: Team(id: "359", name: "Arsenal", league: "eng.1", logo: ""))
    check(missingLogo == [url], "Team ID supplies missing logo URL")

    BadgeProtocol.reset(.success, image: fixture)
    let shared = store("shared")
    let loaded = await withTaskGroup(of: Data?.self) { group in
        for _ in 0..<16 { group.addTask { await shared.load([url]) } }
        var results: [Data?] = []
        for await result in group { results.append(result) }
        return results
    }
    check(loaded.count == 16 && loaded.allSatisfy { $0 != nil }, "All simultaneous badge consumers receive an image")
    check(BadgeProtocol.counts.requests == 1, "Concurrent identical URLs share one request")
    let data = loaded[0]!
    let source = CGImageSourceCreateWithData(data as CFData, nil)!
    let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as! [CFString: Any]
    check(properties[kCGImagePropertyPixelWidth] as? Int == 128, "Badges are downsampled before memory/disk caching")
    let file = await shared.file(for: url)
    check(FileManager.default.fileExists(atPath: file.path), "Successful badge download persists to disk")

    BadgeProtocol.reset(.offline, image: fixture)
    let memory = await shared.load([url])
    check(memory == data && BadgeProtocol.counts.requests == 0, "Fresh memory badge avoids network")
    let restarted = store("shared")
    let disk = await restarted.load([url])
    check(disk == data && BadgeProtocol.counts.requests == 0, "Fresh disk badge survives restart offline")

    let oldDate = Date().addingTimeInterval(-31 * 24 * 3600)
    try FileManager.default.setAttributes([.modificationDate: oldDate], ofItemAtPath: file.path)
    let expired = store("shared")
    let immediate = await expired.cached([url])
    check(immediate == data && BadgeProtocol.counts.requests == 0, "Expired badge is immediately displayable before refresh")
    let fallback = await expired.load([url])
    check(fallback == data && BadgeProtocol.counts.requests == 2, "Expired badge survives failed refresh and one retry")
    let unchanged = try FileManager.default.attributesOfItem(atPath: file.path)[.modificationDate] as! Date
    check(abs(unchanged.timeIntervalSince(oldDate)) < 1, "Failed refresh does not mark old data as fresh")
    _ = await expired.load([url])
    check(BadgeProtocol.counts.requests == 2, "Failure cooldown prevents repeated repaint requests")
    BadgeProtocol.reset(.success, image: fixture)
    await expired.clearFailureCooldown()
    _ = await expired.load([url])
    let updated = try FileManager.default.attributesOfItem(atPath: file.path)[.modificationDate] as! Date
    check(BadgeProtocol.counts.requests == 1 && Date().timeIntervalSince(updated) < 5, "Manual refresh clears cooldown and renews expired cache")

    BadgeProtocol.reset(.firstFails, image: fixture)
    let retry = await store("retry").load([url])
    check(retry != nil && BadgeProtocol.counts.requests == 2, "Transient failure retries successfully")
    BadgeProtocol.reset(.primaryFails, image: fixture)
    let alternative = await store("alternative").load(urls)
    check(alternative != nil && BadgeProtocol.counts.requests == 3, "Broken feed logo falls back to catalog URL")

    BadgeProtocol.reset(.invalidImage, image: fixture)
    let invalid = store("invalid")
    let invalidResult = await invalid.load([url])
    let invalidFile = await invalid.file(for: url)
    check(invalidResult == nil && !FileManager.default.fileExists(atPath: invalidFile.path), "Non-image responses never enter cache")
    BadgeProtocol.reset(.oversized, image: fixture)
    let oversized = await store("oversized").load([url])
    check(oversized == nil, "Oversized response is rejected")
    BadgeProtocol.reset(.success, image: fixture)
    let forbidden = await store("forbidden").load([bad])
    check(forbidden == nil && BadgeProtocol.counts.requests == 0, "Invalid URLs never issue network requests")

    try Data("corrupted PNG".utf8).write(to: file)
    let repaired = await store("shared").load([url])
    check(repaired != nil && BadgeProtocol.counts.requests == 1, "Corrupt disk cache is replaced by a valid download")
    BadgeProtocol.reset(.success, image: fixture)
    let many = store("many")
    await withTaskGroup(of: Void.self) { group in
        for id in 0..<20 {
            group.addTask { _ = await many.load([URL(string: "https://a.espncdn.com/\(id).png")!]) }
        }
    }
    check(BadgeProtocol.counts.requests == 20 && BadgeProtocol.counts.maximumActive <= 6, "Distinct badge downloads are limited to six concurrently")
    BadgeProtocol.reset(.success, image: fixture)
    let cancellation = store("cancelled-consumer")
    let first = Task { await cancellation.load([url]) }
    let second = Task { await cancellation.load([url]) }
    first.cancel()
    let survivor = await second.value
    _ = await first.value
    check(survivor != nil && BadgeProtocol.counts.requests == 1, "Cancelling one consumer does not cancel the shared download")
}
