import Foundation

struct League: Identifiable, Sendable {
    let id: String
    let name: String
    static let all = [League(id: "eng.1", name: "英超"), League(id: "esp.1", name: "西甲"),
                      League(id: "ita.1", name: "意甲"), League(id: "ger.1", name: "德甲"),
                      League(id: "fra.1", name: "法甲"), League(id: "uefa.champions", name: "欧冠")]
    static func name(_ id: String) -> String { all.first { $0.id == id }?.name ?? id }
}

enum Beijing {
    static var calendar: Calendar {
        var value = Calendar(identifier: .gregorian)
        value.timeZone = TimeZone(secondsFromGMT: 8 * 3600)!
        return value
    }
    static func day(_ date: Date = Date()) -> Date { calendar.startOfDay(for: date) }
    static func adding(_ days: Int, to date: Date) -> Date { calendar.date(byAdding: .day, value: days, to: date)! }
    static func format(_ date: Date, _ pattern: String) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "zh_CN")
        formatter.calendar = calendar
        formatter.timeZone = calendar.timeZone
        formatter.dateFormat = pattern
        return formatter.string(from: date)
    }
    // ESPN's date buckets can precede a Beijing date. Fetch the previous bucket too.
    static func queryDates(start: Date, end: Date) -> [String] {
        guard end >= start else { return [] }
        let count = calendar.dateComponents([.day], from: day(start), to: day(end)).day ?? 0
        return (-1...count).map { format(adding($0, to: start), "yyyyMMdd") }
    }
}

struct Team: Codable, Identifiable, Sendable {
    let id: String
    let name: String
    let league: String
    let logo: String
    enum CodingKeys: String, CodingKey { case id = "Id", name = "Name", league = "League", logo = "Logo" }
}

struct MatchEvent: Sendable {
    let clock: String
    let player: String
    let kind: String
}

struct Match: Identifiable, Sendable {
    let id: String
    let league: String
    let kickoff: Date
    let home: Team
    let away: Team
    let homeScore: String
    let awayScore: String
    let state: String
    let status: String
    let venue: String
    let note: String
    let homeStats: [String: String]
    let awayStats: [String: String]
    let events: [MatchEvent]
    var stale = false
    var updated: Date?
    func followed(by ids: [String]) -> Bool { ids.contains(home.id) || ids.contains(away.id) }
}

enum FeedError: LocalizedError {
    case invalidResponse
    var errorDescription: String? { "数据源未返回有效赛程" }
}

enum MatchParser {
    static func object(_ value: Any?) -> [String: Any] { value as? [String: Any] ?? [:] }
    static func items(_ value: Any?) -> [[String: Any]] { value as? [[String: Any]] ?? [] }
    static func string(_ value: Any?) -> String {
        if let value = value as? String { return value }
        if let value = value as? NSNumber { return value.stringValue }
        return ""
    }
    static func score(_ value: Any?) -> String {
        if let score = value as? [String: Any] { return string(score["displayValue"] ?? score["value"]) }
        return string(value)
    }
    static func parse(_ data: Data, league: String, start: Date, end: Date, carryLive: Bool) throws -> [Match] {
        let root = object(try JSONSerialization.jsonObject(with: data))
        guard let events = root["events"] as? [[String: Any]] else { throw FeedError.invalidResponse }
        let iso = ISO8601DateFormatter()
        let fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let minutePrecision = DateFormatter()
        minutePrecision.locale = Locale(identifier: "en_US_POSIX")
        minutePrecision.calendar = Calendar(identifier: .gregorian)
        minutePrecision.timeZone = TimeZone(secondsFromGMT: 0)
        minutePrecision.dateFormat = "yyyy-MM-dd'T'HH:mmXXXXX"
        var result: [Match] = []
        for event in events {
            guard let comp = items(event["competitions"]).first,
                  let kickoff = iso.date(from: string(event["date"])) ?? fractional.date(from: string(event["date"])) ?? minutePrecision.date(from: string(event["date"])) else { continue }
            let status = object(comp["status"] ?? event["status"])
            let type = object(status["type"])
            let state = string(type["state"])
            let localDay = Beijing.day(kickoff)
            guard (localDay >= Beijing.day(start) && localDay <= Beijing.day(end)) ||
                    (carryLive && state == "in" && localDay == Beijing.adding(-1, to: Beijing.day(start))) else { continue }
            let sides = items(comp["competitors"])
            guard let home = sides.first(where: { string($0["homeAway"]) == "home" }),
                  let away = sides.first(where: { string($0["homeAway"]) == "away" }) else { continue }
            func team(_ side: [String: Any]) -> Team {
                let value = object(side["team"])
                return Team(id: string(side["id"] ?? value["id"]), name: string(value["displayName"]),
                            league: league, logo: string(value["logo"]))
            }
            func stats(_ side: [String: Any]) -> [String: String] {
                Dictionary(items(side["statistics"]).map { (string($0["name"]), string($0["displayValue"])) }, uniquingKeysWith: { _, new in new })
            }
            let statuses = ["STATUS_POSTPONED": "延期", "STATUS_CANCELED": "取消", "STATUS_SUSPENDED": "中断",
                            "STATUS_ABANDONED": "腰斩", "STATUS_HALFTIME": "中场", "STATUS_FINAL_PEN": "点球结束", "STATUS_FINAL_AET": "加时结束"]
            let clock = string(status["displayClock"])
            let label = statuses[string(type["name"])] ?? (state == "post" ? "已结束" : state == "in" ? (clock.isEmpty ? "进行中" : clock) : "未开始")
            let details: [MatchEvent] = items(comp["details"]).compactMap { detail in
                let scoring = detail["scoringPlay"] as? Bool == true
                let kind = scoring ? (detail["shootout"] as? Bool == true ? "点球大战" : detail["ownGoal"] as? Bool == true ? "乌龙球" : detail["penaltyKick"] as? Bool == true ? "点球" : "进球") : detail["redCard"] as? Bool == true ? "红牌" : detail["yellowCard"] as? Bool == true ? "黄牌" : ""
                guard !kind.isEmpty else { return nil }
                return MatchEvent(clock: string(object(detail["clock"])["displayValue"]), player: string(items(detail["athletesInvolved"]).first?["displayName"]), kind: kind)
            }
            let id = string(event["id"])
            guard !id.isEmpty else { continue }
            result.append(Match(id: id, league: league, kickoff: kickoff, home: team(home), away: team(away),
                                homeScore: score(home["score"]), awayScore: score(away["score"]), state: state, status: label,
                                venue: string(object(comp["venue"])["fullName"]), note: comp["timeValid"] as? Bool == false ? "时间待定" : "",
                                homeStats: stats(home), awayStats: stats(away), events: details))
        }
        return unique(result)
    }
    static func unique(_ matches: [Match]) -> [Match] {
        Dictionary(matches.map { ($0.id, $0) }, uniquingKeysWith: { old, new in
            if old.stale != new.stale { return old.stale ? new : old }
            return (old.updated ?? .distantPast) > (new.updated ?? .distantPast) ? old : new
        }).values.sorted { $0.kickoff == $1.kickoff ? $0.id < $1.id : $0.kickoff < $1.kickoff }
    }
    static func select(_ matches: [Match], favorites: [String], onlyFavorites: Bool, league: String, liveOnly: Bool) -> [Match] {
        unique(matches).filter { (league == "all" || $0.league == league) && (!liveOnly || $0.state == "in") && (!onlyFavorites || $0.followed(by: favorites)) }
    }
}

struct FeedResult: Sendable {
    var matches: [Match] = []
    var failed = 0
    var updated: Date?
}

struct ScoreFeed: Sendable {
    let cache: URL
    let session: URLSession
    init(cache: URL, session: URLSession = ScoreFeed.defaultSession) {
        self.cache = cache
        self.session = session
    }
    static let defaultSession: URLSession = {
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 18
        config.timeoutIntervalForResource = 22
        config.httpAdditionalHeaders = ["User-Agent": "TouchlineDesktop-macOS/1.3"]
        return URLSession(configuration: config)
    }()
    func load(start: Date, end: Date, force: Bool) async -> FeedResult {
        await withTaskGroup(of: FeedResult.self) { group in
            for league in League.all {
                group.addTask {
                    var result = FeedResult()
                    for date in Beijing.queryDates(start: start, end: end) {
                        if Task.isCancelled { break }
                        let part = await loadDay(league: league.id, date: date, start: start, end: end, force: force)
                        result.matches += part.matches
                        result.failed += part.failed
                        if let updated = part.updated { result.updated = min(result.updated ?? updated, updated) }
                    }
                    return result
                }
            }
            var result = FeedResult()
            for await part in group {
                result.matches += part.matches
                result.failed += part.failed
                if let updated = part.updated { result.updated = min(result.updated ?? updated, updated) }
            }
            result.matches = MatchParser.unique(result.matches)
            return result
        }
    }
    private func loadDay(league: String, date: String, start: Date, end: Date, force: Bool) async -> FeedResult {
        let file = cache.appendingPathComponent("\(league)_\(date).json")
        let modified = (try? file.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate
        let carry = Beijing.day(start) == Beijing.day()
        func parse(_ data: Data) throws -> [Match] {
            try MatchParser.parse(data, league: league, start: start, end: end, carryLive: carry)
        }
        do {
            let now = Date()
            // Refresh all buckets near today, even across the UTC/Beijing midnight boundary.
            let recent = date >= Beijing.format(Beijing.adding(-2, to: now), "yyyyMMdd") && date <= Beijing.format(now, "yyyyMMdd")
            let ttl: TimeInterval = date > Beijing.format(now, "yyyyMMdd") ? 900 : 21600
            if !force, !recent, let modified, now.timeIntervalSince(modified) < ttl,
               let data = try? Data(contentsOf: file), let cached = try? parse(data) {
                return FeedResult(matches: cached.map { var m = $0; m.updated = modified; return m }, updated: modified)
            }
            let url = URL(string: "https://site.api.espn.com/apis/site/v2/sports/soccer/\(league)/scoreboard?dates=\(date)&limit=100")!
            let (data, response) = try await session.data(from: url)
            guard let response = response as? HTTPURLResponse, (200..<300).contains(response.statusCode) else { throw FeedError.invalidResponse }
            let matches = try parse(data)
            try? FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
            try? data.write(to: file, options: .atomic)
            return FeedResult(matches: matches.map { var m = $0; m.updated = now; return m }, updated: now)
        } catch {
            let matches = (try? Data(contentsOf: file)).flatMap { try? parse($0) } ?? []
            return FeedResult(matches: matches.map { var m = $0; m.stale = true; m.updated = modified; return m }, failed: 1, updated: modified)
        }
    }
}

struct ScoreTracker {
    var previous: [String: Match] = [:]
    mutating func observe(_ matches: [Match], name: (String) -> String) -> [String] {
        var changes: [String] = []
        for match in matches where !match.stale {
            if let old = previous[match.id], old.state == "in" || match.state == "in",
               let h = Int(match.homeScore), let a = Int(match.awayScore), let oh = Int(old.homeScore), let oa = Int(old.awayScore), h != oh || a != oa {
                changes.append("\(h < oh || a < oa ? "比分更正" : "进球") · \(name(match.home.name)) \(h) : \(a) \(name(match.away.name))")
            }
            previous[match.id] = match
        }
        return changes
    }
}
