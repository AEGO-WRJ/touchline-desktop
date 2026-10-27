import AppKit
import Combine
import ServiceManagement

struct Preferences: Codable {
    var favorites: [String] = []
    var onlyFavorites = false
    var pinned = true
    var compact = false
    var sound = false
    var startInMenuBar = true
    var opacity = 1.0
    var compactOpacity = 0.65
    var x: Double?
    var y: Double?
    var width = 420.0
    var height = 740.0
    enum CodingKeys: String, CodingKey { case favorites, onlyFavorites, pinned, compact, sound, startInMenuBar, opacity, compactOpacity, x, y, width, height }
    init() {}
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        favorites = Array(NSOrderedSet(array: try c.decodeIfPresent([String].self, forKey: .favorites) ?? [])) as? [String] ?? []
        onlyFavorites = try c.decodeIfPresent(Bool.self, forKey: .onlyFavorites) ?? false
        pinned = try c.decodeIfPresent(Bool.self, forKey: .pinned) ?? true
        compact = try c.decodeIfPresent(Bool.self, forKey: .compact) ?? false
        sound = try c.decodeIfPresent(Bool.self, forKey: .sound) ?? false
        startInMenuBar = try c.decodeIfPresent(Bool.self, forKey: .startInMenuBar) ?? true
        opacity = min(1, max(0.1, try c.decodeIfPresent(Double.self, forKey: .opacity) ?? 1))
        compactOpacity = min(1, max(0.1, try c.decodeIfPresent(Double.self, forKey: .compactOpacity) ?? 0.65))
        x = try c.decodeIfPresent(Double.self, forKey: .x)
        y = try c.decodeIfPresent(Double.self, forKey: .y)
        width = min(640, max(400, try c.decodeIfPresent(Double.self, forKey: .width) ?? 420))
        height = min(1100, max(480, try c.decodeIfPresent(Double.self, forKey: .height) ?? 740))
    }
}

struct SettingsStore {
    let directory: URL
    var file: URL { directory.appendingPathComponent("settings.json") }
    var backup: URL { directory.appendingPathComponent("settings.json.bak") }
    func load() -> (Preferences, String?) {
        for url in [file, backup] {
            if let data = try? Data(contentsOf: url), let preferences = try? JSONDecoder().decode(Preferences.self, from: data) {
                return (preferences, url == backup ? "设置已从备份恢复。" : nil)
            }
        }
        let damaged = FileManager.default.fileExists(atPath: file.path)
        if damaged {
            // Keep a recovery copy before future preference writes replace the damaged file.
            try? FileManager.default.copyItem(at: file, to: directory.appendingPathComponent("settings-damaged-\(Int(Date().timeIntervalSince1970)).json"))
        }
        return (Preferences(), damaged ? "设置无法读取，已使用默认值并保留原文件。" : nil)
    }
    func save(_ value: Preferences) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(value)
        try data.write(to: file, options: .atomic)
        try data.write(to: backup, options: .atomic)
    }
}

@MainActor
final class AppModel: ObservableObject {
    @Published var preferences: Preferences { didSet { save(); presentationChanged?() } }
    @Published var matches: [Match] = []
    @Published var teams: [Team] = []
    @Published var league = "all"
    @Published var liveOnly = false
    @Published var loading = false
    @Published var footer = "正在连接 ESPN…"
    @Published var warning: String?
    @Published var notice: String?
    @Published var start = Beijing.day()
    @Published var end = Beijing.adding(6, to: Beijing.day())
    @Published var showFavorites = false
    @Published var showSettings = false
    @Published var loginEnabled = SMAppService.mainApp.status == .enabled
    @Published var loginMessage = ""
    @Published var todayScroll = UUID()
    @Published var badgeRevision = 0
    @Published private(set) var fullCardVisible = false
    let store: SettingsStore
    let feed: ScoreFeed
    let badges: BadgeStore
    var presentationChanged: (() -> Void)?
    private var names: [String: String] = [:]
    private var catalogLogos: [String: [String]] = [:]
    private var timer: Timer?
    private var refreshTask: Task<Void, Never>?
    private var tracker = ScoreTracker()
    private var nextRefresh = Date.distantPast
    private var lastDay = Beijing.day()
    private var pendingForce = false
    private var noticeExpires = Date.distantPast

    init(dataDirectory: URL? = nil, cacheDirectory: URL? = nil) {
        let fm = FileManager.default
        store = SettingsStore(directory: dataDirectory ?? fm.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("TouchlineCard"))
        let cache = cacheDirectory ?? fm.urls(for: .cachesDirectory, in: .userDomainMask)[0].appendingPathComponent("TouchlineCard")
        feed = ScoreFeed(cache: cache)
        badges = BadgeStore(directory: cache.appendingPathComponent("badges"))
        let loaded = store.load()
        preferences = loaded.0
        warning = loaded.1
        if let url = Bundle.main.url(forResource: "teams", withExtension: "json"), let data = try? Data(contentsOf: url),
           let values = try? JSONDecoder().decode([Team].self, from: data) {
            teams = Dictionary(values.map { ($0.id, $0) }, uniquingKeysWith: { old, _ in old }).values.sorted { $0.name < $1.name }
            for team in values { catalogLogos[team.id, default: []].append(team.logo) }
        }
        if let url = Bundle.main.url(forResource: "team-names.zh-Hans", withExtension: "json"), let data = try? Data(contentsOf: url) {
            names = (try? JSONDecoder().decode([String: String].self, from: data)) ?? [:]
        }
    }
    func name(_ value: String) -> String { names[value] ?? value }
    var primaryTeam: Team? { teams.first { $0.id == preferences.favorites.first } }
    func badgeURLs(_ team: Team?) -> [URL] { BadgeSource.urls(team: team, catalog: team.flatMap { catalogLogos[$0.id] } ?? []) }
    var visibleMatches: [Match] {
        MatchParser.select(matches, favorites: preferences.favorites, onlyFavorites: preferences.onlyFavorites, league: league, liveOnly: liveOnly)
    }
    var liveCount: Int { visibleMatches.filter { $0.state == "in" }.count }
    var days: [Date] {
        let first = min(start, visibleMatches.first.map { Beijing.day($0.kickoff) } ?? start)
        let count = Beijing.calendar.dateComponents([.day], from: first, to: end).day ?? 0
        return (0...max(0, count)).map { Beijing.adding($0, to: first) }
    }
    func begin() {
        if fullCardVisible { refresh() }
        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }
    func stop() { timer?.invalidate(); refreshTask?.cancel(); save() }
    func setFullCardVisible(_ visible: Bool, force: Bool = false) {
        guard fullCardVisible != visible else {
            if visible && force { refresh(force: true) }
            return
        }
        fullCardVisible = visible
        if visible {
            advanceDay()
            nextRefresh = .distantPast
            refresh(force: force)
        } else {
            refreshTask?.cancel()
            refreshTask = nil
            loading = false
            pendingForce = false
            footer = "已暂停刷新 · 展开完整卡片后同步"
        }
    }
    func tick() {
        if notice != nil && Date() > noticeExpires { notice = nil }
        guard fullCardVisible else { return }
        advanceDay()
        if !loading && Date() >= nextRefresh { refresh() }
    }
    private func advanceDay() {
        let today = Beijing.day()
        if today != lastDay {
            if start == lastDay { start = today; end = Beijing.adding(6, to: today) }
            lastDay = today
            nextRefresh = .distantPast
        }
    }
    func refresh(force: Bool = false) {
        guard fullCardVisible else { return }
        if loading { pendingForce = pendingForce || force; return }
        loading = true
        footer = "正在同步赛程与比分…"
        let requestedStart = start, requestedEnd = end
        refreshTask = Task { [weak self, feed] in
            if force { await self?.badges.clearFailureCooldown() }
            let result = await feed.load(start: requestedStart, end: requestedEnd, force: force)
            guard let self, !Task.isCancelled else { return }
            if self.start == requestedStart && self.end == requestedEnd {
                self.matches = result.matches
                var known = Dictionary(self.teams.map { ($0.id, $0) }, uniquingKeysWith: { old, _ in old })
                for m in result.matches { known[m.home.id] = m.home; known[m.away.id] = m.away }
                self.teams = known.values.sorted { self.name($0.name) < self.name($1.name) }
                let changes = self.tracker.observe(result.matches, name: self.name)
                if !changes.isEmpty {
                    self.notice = changes.joined(separator: "\n")
                    self.noticeExpires = Date().addingTimeInterval(15)
                    if self.preferences.sound { NSSound(named: "Glass")?.play() }
                }
                if result.failed > 0 {
                    self.footer = result.matches.isEmpty ? "暂时无法获取赛程 · 稍后自动重试" : "部分数据未更新 · 缓存比分可能已过期"
                } else {
                    self.footer = "更新于 \(Beijing.format(Date(), "HH:mm:ss"))"
                }
            }
            self.loading = false
            self.badgeRevision += 1
            self.nextRefresh = Date().addingTimeInterval(self.matches.contains { $0.state == "in" } ? 30 : 120)
            let again = self.start != requestedStart || self.end != requestedEnd || self.pendingForce
            let forceAgain = self.pendingForce
            self.pendingForce = false
            if again { self.refresh(force: forceAgain) }
        }
    }
    func today() {
        let day = Beijing.day()
        if start > day || end < day { start = day; end = Beijing.adding(6, to: day); refresh() }
        todayScroll = UUID()
    }
    func shiftWeek(_ delta: Int) {
        selectDate(Beijing.adding(delta * 7, to: start))
    }
    func selectDate(_ date: Date) {
        start = Beijing.day(date)
        end = Beijing.adding(6, to: start)
        matches = []
        refresh()
    }
    func follow(_ id: String) {
        if preferences.favorites.contains(id) { preferences.favorites.removeAll { $0 == id } }
        else { preferences.favorites.append(id) }
    }
    func moveFavorite(_ id: String, by offset: Int) {
        guard let index = preferences.favorites.firstIndex(of: id), preferences.favorites.indices.contains(index + offset) else { return }
        preferences.favorites.swapAt(index, index + offset)
    }
    func setLogin(_ enabled: Bool) {
        do {
            if enabled { try SMAppService.mainApp.register() }
            else { try SMAppService.mainApp.unregister() }
            loginEnabled = SMAppService.mainApp.status == .enabled
            loginMessage = SMAppService.mainApp.status == .requiresApproval ? "请在系统设置 → 通用 → 登录项中允许看球。" : ""
        } catch {
            loginEnabled = SMAppService.mainApp.status == .enabled
            loginMessage = "设置失败：\(error.localizedDescription)"
        }
    }
    func updateLoginStatus() { loginEnabled = SMAppService.mainApp.status == .enabled }
    private func save() {
        do { try store.save(preferences) }
        catch { warning = "设置保存失败：\(error.localizedDescription)" }
    }
}
