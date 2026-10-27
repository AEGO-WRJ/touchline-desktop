import Foundation

var checks = 0
func check(_ condition: @autoclosure () -> Bool, _ description: String) {
    guard condition() else { fputs("FAIL: \(description)\n", stderr); exit(1) }
    checks += 1
}
func date(_ text: String) -> Date { ISO8601DateFormatter().date(from: text)! }
func event(_ id: String, _ kickoff: String, state: String = "pre", status: String = "STATUS_SCHEDULED", homeScore: Any = "0", awayScore: Any = "0") -> [String: Any] {
    ["id": id, "date": kickoff, "competitions": [[
        "status": ["type": ["state": state, "name": status], "displayClock": "45'+2'"],
        "competitors": [
            ["id": "359", "homeAway": "home", "team": ["id": "359", "displayName": "Arsenal"], "score": homeScore,
             "statistics": [["name": "possessionPct", "displayValue": "61"]]],
            ["id": "364", "homeAway": "away", "team": ["id": "364", "displayName": "Liverpool"], "score": awayScore]
        ],
        "details": [["scoringPlay": true, "ownGoal": true, "clock": ["displayValue": "31'"], "athletesInvolved": [["displayName": "Player"]]]]
    ]]]
}
func payload(_ events: [[String: Any]]) -> Data { try! JSONSerialization.data(withJSONObject: ["events": events]) }

final class StubProtocol: URLProtocol {
    static var data = Data()
    static var offline = false
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        if Self.offline { client?.urlProtocol(self, didFailWithError: URLError(.notConnectedToInternet)); return }
        let response = HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: Self.data)
        client?.urlProtocolDidFinishLoading(self)
    }
    override func stopLoading() {}
}

func runTests() async throws {
    // Keep date behavior independent of the Mac's configured timezone and DST.
    NSTimeZone.default = TimeZone(identifier: "America/Los_Angeles")!
    let start = Beijing.day(date("2026-10-27T01:00:00Z"))
    check(Beijing.format(start, "yyyyMMdd HH:mm") == "20261027 00:00", "Beijing day is independent of host timezone")
    check(Beijing.queryDates(start: start, end: start) == ["20261026", "20261027"], "Both UTC buckets requested")
    check(Beijing.queryDates(start: start, end: Beijing.adding(-1, to: start)).isEmpty, "Invalid range is empty")
    let newYear = Beijing.day(date("2027-01-01T02:00:00Z"))
    check(Beijing.queryDates(start: newYear, end: newYear) == ["20261231", "20270101"], "Year rollover")
    let data = payload([
        event("previous", "2026-10-26T15:59:00Z"),
        event("carry", "2026-10-26T15:00:00Z", state: "in"),
        event("midnight", "2026-10-26T16:00:00Z", state: "in", status: "STATUS_HALFTIME", homeScore: 2, awayScore: ["displayValue": "1"]),
        event("end", "2026-10-27T15:59:59Z"),
        event("tomorrow", "2026-10-27T16:00:00Z")
    ])
    let parsed = try MatchParser.parse(data, league: "eng.1", start: start, end: start, carryLive: true)
    check(parsed.map(\.id) == ["carry", "midnight", "end"], "Midnight boundaries and previous-day live carry")
    let noCarry = try MatchParser.parse(data, league: "eng.1", start: start, end: start, carryLive: false)
    check(noCarry.map(\.id) == ["midnight", "end"], "Historical range excludes previous live game")
    let match = parsed[1]
    check(match.homeScore == "2" && match.awayScore == "1", "Numeric and object scores")
    check(match.status == "中场", "Halftime status")
    check(match.events.first?.kind == "乌龙球", "Own-goal event")
    check(match.homeStats["possessionPct"] == "61", "Match statistics")
    let shortDate = try MatchParser.parse(payload([event("short", "2026-10-27T14:00Z")]), league: "eng.1", start: start, end: start, carryLive: false)
    check(shortDate.count == 1 && Beijing.format(shortDate[0].kickoff, "HH:mm") == "22:00", "ESPN minute-precision timestamp")
    let fractionalDate = try MatchParser.parse(payload([event("fraction", "2026-10-27T14:00:00.000Z")]), league: "eng.1", start: start, end: start, carryLive: false)
    check(fractionalDate.count == 1, "Fractional-second timestamp")
    let noMatches = try MatchParser.parse(payload([]), league: "eng.1", start: start, end: start, carryLive: false)
    check(noMatches.isEmpty, "An empty scoreboard is valid")
    do {
        _ = try MatchParser.parse(Data("{}".utf8), league: "eng.1", start: start, end: start, carryLive: false)
        check(false, "Invalid payload must fail")
    } catch { checks += 1 }
    check(MatchParser.select(parsed, favorites: [], onlyFavorites: true, league: "all", liveOnly: false).isEmpty, "No favorites does not show all games")
    check(MatchParser.select(parsed, favorites: ["364"], onlyFavorites: true, league: "eng.1", liveOnly: true).count == 2, "Away-team favorites and live filter combine")
    check(MatchParser.select(parsed, favorites: [], onlyFavorites: false, league: "esp.1", liveOnly: false).isEmpty, "League filtering")
    var stale = match
    stale.stale = true
    check(MatchParser.unique([match, stale]).first?.stale == false, "Fresh duplicate wins over stale data")
    check(MatchParser.unique([stale, match]).count == 1, "Duplicate IDs collapse")

    var tracker = ScoreTracker()
    check(tracker.observe([match], name: { $0 }).isEmpty, "Initial live scores do not announce goals")
    let changed = try MatchParser.parse(payload([event("midnight", "2026-10-26T16:00:00Z", state: "in", homeScore: "3", awayScore: "1")]), league: "eng.1", start: start, end: start, carryLive: false)[0]
    check(tracker.observe([changed], name: { $0 }).first?.hasPrefix("进球") == true, "Score increase announces goal")
    check(tracker.observe([stale], name: { $0 }).isEmpty, "Stale scores never announce corrections")
    check(tracker.observe([match], name: { $0 }).first?.hasPrefix("比分更正") == true, "Fresh decrease announces correction")

    let temp = FileManager.default.temporaryDirectory.appendingPathComponent("touchline-tests-\(UUID().uuidString)")
    defer { try? FileManager.default.removeItem(at: temp) }
    let store = SettingsStore(directory: temp.appendingPathComponent("settings"))
    check(store.load().0.pinned, "First launch defaults")
    var preferences = Preferences()
    preferences.favorites = ["364", "359"]
    preferences.compact = true
    preferences.opacity = 0.9
    preferences.compactOpacity = 0.3
    try store.save(preferences)
    let restored = store.load().0
    check(restored.favorites == ["364", "359"], "Favorite order survives restart")
    check(restored.opacity == 0.9 && restored.compactOpacity == 0.3 && restored.compact, "Presentation and separate opacities survive restart")
    try Data("broken".utf8).write(to: store.file)
    check(store.load().0.favorites == preferences.favorites && store.load().1 != nil, "Damaged primary restores backup with notice")
    let sparse = try JSONDecoder().decode(Preferences.self, from: Data("{\"favorites\":[\"359\",\"359\"],\"opacity\":5,\"width\":-10}".utf8))
    check(sparse.favorites == ["359"] && sparse.opacity == 1 && sparse.width == 400, "Older settings get defaults, deduplication and bounds")

    let config = URLSessionConfiguration.ephemeral
    config.protocolClasses = [StubProtocol.self]
    let session = URLSession(configuration: config)
    defer { session.invalidateAndCancel() }
    let feed = ScoreFeed(cache: temp.appendingPathComponent("cache"), session: session)
    StubProtocol.data = payload([event("live", "2026-10-27T10:00:00Z", state: "in")])
    let fresh = await feed.load(start: start, end: start, force: true)
    check(fresh.failed == 0 && fresh.matches.count == 1 && !fresh.matches[0].stale, "Successful feed writes cache and merges buckets")
    StubProtocol.offline = true
    let offline = await feed.load(start: start, end: start, force: true)
    check(offline.failed == 12 && offline.matches.count == 1 && offline.matches[0].stale, "Network outage falls back to marked cached scores")
    check(offline.matches[0].updated != nil, "Cache age remains available offline")
    let emptyFeed = ScoreFeed(cache: temp.appendingPathComponent("empty"), session: session)
    let empty = await emptyFeed.load(start: start, end: start, force: true)
    check(empty.failed == 12 && empty.matches.isEmpty, "First launch offline is reported without fake scores")
    StubProtocol.offline = false
    StubProtocol.data = Data("{}".utf8)
    let malformed = await feed.load(start: start, end: start, force: true)
    check(malformed.failed == 12 && malformed.matches.first?.stale == true, "Malformed responses do not replace valid cache")
    print("Passed \(checks) macOS checks: dates, parsing, filters, score changes, settings and offline cache.")
}

Task {
    do { try await runTests(); exit(0) }
    catch { fputs("FAIL: \(error)\n", stderr); exit(1) }
}
dispatchMain()
