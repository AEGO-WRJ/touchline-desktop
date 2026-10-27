import SwiftUI
import AppKit
import ServiceManagement

// Use the property wrapper available on macOS 13, including with SDKs that
// also export a newer State macro requiring Xcode's macro plug-in.
private typealias ViewState<Value> = SwiftUI.State<Value>

private let ink = Color(red: 0.92, green: 0.95, blue: 0.96)
private let muted = Color(red: 0.57, green: 0.65, blue: 0.69)
private let green = Color(red: 0.39, green: 0.91, blue: 0.68)
private let shell = Color(red: 0.067, green: 0.11, blue: 0.135)

struct DragArea: NSViewRepresentable {
    final class DragView: NSView {
        override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
    }
    func makeNSView(context: Context) -> DragView { DragView() }
    func updateNSView(_ view: DragView, context: Context) {}
}

struct Badge: View {
    @EnvironmentObject private var model: AppModel
    @ViewState private var bitmap: NSImage?
    let team: Team?
    var size: CGFloat = 25
    private struct Request: Equatable { let urls: [URL]; let revision: Int }
    var body: some View {
        let request = Request(urls: model.badgeURLs(team), revision: model.badgeRevision)
        Group {
            if let bitmap { Image(nsImage: bitmap).resizable().scaledToFit() }
            else { Image(systemName: "soccerball").resizable().scaledToFit().foregroundColor(green) }
        }
        .frame(width: size, height: size)
        .accessibilityHidden(true)
        .task(id: request) {
            let cached = await model.badges.cached(request.urls)
            guard !Task.isCancelled else { return }
            bitmap = cached.flatMap(NSImage.init(data:))
            let loaded = await model.badges.load(request.urls)
            guard !Task.isCancelled else { return }
            if let loaded { bitmap = NSImage(data: loaded) }
        }
    }
}

struct CardButton: ButtonStyle {
    var active = false
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 11, weight: .medium))
            .padding(.horizontal, 9).padding(.vertical, 7)
            .foregroundColor(active ? green : ink)
            .background(active ? green.opacity(0.13) : Color.white.opacity(configuration.isPressed ? 0.14 : 0.055))
            .clipShape(RoundedRectangle(cornerRadius: 7))
    }
}

struct CardView: View {
    @ObservedObject var model: AppModel
    let hide: () -> Void
    @ViewState private var showOpacity = false
    var body: some View {
        Group {
            if model.preferences.compact { compact }
            else { full }
        }
        .foregroundColor(ink).background(shell)
        .clipShape(RoundedRectangle(cornerRadius: model.preferences.compact ? 10 : 18))
        .overlay(RoundedRectangle(cornerRadius: model.preferences.compact ? 10 : 18).stroke(Color.white.opacity(0.14), lineWidth: 1))
        .preferredColorScheme(.dark)
        .sheet(isPresented: $model.showFavorites) { FavoritesView(model: model) }
        .sheet(isPresented: $model.showSettings) { SettingsView(model: model) }
    }
    private var compact: some View {
        HStack(spacing: 8) {
            HStack(spacing: 8) {
                Badge(team: model.primaryTeam, size: 20)
                Text("看球 · 已暂停刷新").font(.system(size: 11, weight: .semibold))
                Spacer(minLength: 0)
            }.overlay(DragArea()).help("拖动移动卡片")
            Button { showOpacity.toggle() } label: { Image(systemName: "circle.lefthalf.filled") }
                .buttonStyle(.plain).help("收纳条透明度")
                .popover(isPresented: $showOpacity) {
                    VStack(alignment: .leading) {
                        Text("收纳条不透明度 \(Int(model.preferences.compactOpacity * 100))%")
                        Slider(value: $model.preferences.compactOpacity, in: 0.1...1)
                    }.padding().frame(width: 230)
                }
            Button("展开") { model.preferences.compact = false }.buttonStyle(CardButton())
        }.padding(.horizontal, 10).frame(maxWidth: .infinity, maxHeight: .infinity)
    }
    private var full: some View {
        VStack(spacing: 13) {
            HStack(alignment: .top, spacing: 8) {
                VStack(alignment: .leading, spacing: 5) {
                    Text("TOUCHLINE  /  比分随行").font(.system(size: 9, weight: .semibold)).foregroundColor(green)
                    HStack(spacing: 8) {
                        if model.primaryTeam != nil { Badge(team: model.primaryTeam, size: 28) }
                        Text("看球").font(.system(size: 26, weight: .bold))
                    }
                }.frame(maxWidth: .infinity, alignment: .leading).overlay(DragArea()).help("拖动移动卡片")
                Button { model.preferences.pinned.toggle() } label: { Image(systemName: model.preferences.pinned ? "pin.fill" : "pin") }
                    .buttonStyle(CardButton(active: model.preferences.pinned)).help("切换窗口置顶")
                Button("收纳") { model.preferences.compact = true }.buttonStyle(CardButton())
                Button(action: hide) { Image(systemName: "minus") }.buttonStyle(CardButton()).help("隐藏到菜单栏")
                Button(action: hide) { Image(systemName: "xmark") }.buttonStyle(CardButton()).help("关闭卡片，继续在菜单栏运行")
            }
            HStack {
                Button("今天", action: model.today).buttonStyle(CardButton(active: true))
                Button { model.shiftWeek(-1) } label: { Image(systemName: "chevron.left") }.help("上一周")
                Button { model.shiftWeek(1) } label: { Image(systemName: "chevron.right") }.help("下一周")
                DatePicker("赛程起始日期", selection: Binding(get: { model.start }, set: model.selectDate), displayedComponents: .date)
                    .datePickerStyle(.field).labelsHidden().frame(width: 108)
                    .environment(\.timeZone, Beijing.calendar.timeZone).environment(\.calendar, Beijing.calendar)
                    .help("选择一周赛程的起始日期")
                Spacer()
                Button("关注球队") { model.showFavorites = true }.buttonStyle(CardButton())
            }.buttonStyle(CardButton())
            HStack(spacing: 3) {
                tab("all", "全部")
                ForEach(League.all) { tab($0.id, $0.name) }
            }
            HStack {
                Text("\(model.visibleMatches.count) 场比赛 · \(model.liveCount) 场进行中").font(.system(size: 11)).foregroundColor(muted)
                Spacer()
                Button("只看进行中") { model.liveOnly.toggle() }.buttonStyle(CardButton(active: model.liveOnly))
            }
            HStack {
                Toggle("只看关注球队", isOn: $model.preferences.onlyFavorites).toggleStyle(.checkbox)
                Spacer()
                Text("北京时间 UTC+8").foregroundColor(muted)
            }.font(.system(size: 10))
            if let warning = model.warning {
                HStack(alignment: .top) {
                    Text(warning)
                    Spacer()
                    Button { model.warning = nil } label: { Image(systemName: "xmark") }.buttonStyle(.plain)
                }.font(.system(size: 10)).foregroundColor(.orange)
            }
            if let notice = model.notice {
                Text(notice).font(.system(size: 11)).foregroundColor(green).frame(maxWidth: .infinity, alignment: .leading)
                    .padding(9).background(green.opacity(0.1)).cornerRadius(8)
            }
            schedule
            VStack(alignment: .leading, spacing: 7) {
                HStack(spacing: 8) {
                    if model.loading { ProgressView().controlSize(.mini) }
                    Text(model.footer).font(.system(size: 10)).foregroundColor(muted).frame(maxWidth: .infinity, alignment: .leading)
                    Button { model.refresh(force: true) } label: { Image(systemName: "arrow.clockwise") }
                        .buttonStyle(CardButton()).disabled(model.loading).help("立即刷新（⌘R）")
                    Button("设置") { model.showSettings = true }.buttonStyle(CardButton())
                }
                Text("数据：ESPN · 可能延迟 · 点击比赛查看数据").font(.system(size: 9)).foregroundColor(muted)
            }
        }.padding(18).frame(maxWidth: .infinity, maxHeight: .infinity)
    }
    private func tab(_ id: String, _ title: String) -> some View {
        Button { model.league = id } label: { Text(title).frame(maxWidth: .infinity) }
            .buttonStyle(CardButton(active: model.league == id))
    }
    private var schedule: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 9) {
                    ForEach(model.days, id: \.self) { day in
                        let matches = model.visibleMatches.filter { Beijing.day($0.kickoff) == day }
                        VStack(alignment: .leading, spacing: 8) {
                            HStack {
                                Text(Beijing.format(day, "MM月dd日 EEEE")).font(.system(size: 11, weight: .semibold))
                                if day == Beijing.day() { Text("今天").font(.system(size: 9)).foregroundColor(green) }
                                Spacer()
                                Text("\(matches.count) 场").font(.system(size: 9)).foregroundColor(muted)
                            }.padding(.top, 5)
                            if matches.isEmpty {
                                Text(model.loading ? "正在读取赛程…" : model.preferences.onlyFavorites && model.preferences.favorites.isEmpty ? "先添加关注球队，再查看赛程" : "当前筛选下暂无比赛")
                                    .font(.system(size: 11)).foregroundColor(muted).padding(.vertical, 10)
                            }
                            ForEach(matches) { MatchRow(match: $0, model: model) }
                        }.id(day)
                    }
                }
            }
            .onChange(of: model.todayScroll) { _ in withAnimation { proxy.scrollTo(Beijing.day(), anchor: .top) } }
            .onChange(of: model.start) { day in proxy.scrollTo(day, anchor: .top) }
        }
    }
}

struct MatchRow: View {
    let match: Match
    @ObservedObject var model: AppModel
    @ViewState private var expanded = false
    var body: some View {
        VStack(spacing: 9) {
            Button { expanded.toggle() } label: {
                VStack(spacing: 10) {
                    HStack {
                        Text(League.name(match.league)).foregroundColor(green)
                        Text(Beijing.format(match.kickoff, "HH:mm")).foregroundColor(muted)
                        Spacer()
                        if match.stale { Text("缓存").foregroundColor(.orange) }
                        Text(match.note.isEmpty ? match.status : match.note).foregroundColor(match.state == "in" ? green : muted)
                        Image(systemName: expanded ? "chevron.up" : "chevron.down").foregroundColor(muted)
                    }.font(.system(size: 10))
                    HStack(spacing: 10) {
                        VStack(alignment: .leading, spacing: 9) {
                            teamLine(match.home)
                            teamLine(match.away)
                        }
                        Spacer(minLength: 6)
                        VStack(spacing: 9) {
                            score(match.homeScore)
                            score(match.awayScore)
                        }.font(.system(size: 17, weight: .bold, design: .rounded)).monospacedDigit()
                    }
                }.contentShape(Rectangle())
            }.buttonStyle(.plain)
            if expanded { details }
        }
        .padding(12)
        .background(Color.white.opacity(match.followed(by: model.preferences.favorites) ? 0.065 : 0.035))
        .cornerRadius(10)
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(match.followed(by: model.preferences.favorites) ? green.opacity(0.3) : Color.white.opacity(0.06)))
    }
    private func teamLine(_ team: Team) -> some View {
        HStack(spacing: 7) {
            Badge(team: team, size: 19)
            Text(model.name(team.name)).font(.system(size: 12, weight: .medium)).lineLimit(2).multilineTextAlignment(.leading)
            if model.preferences.favorites.contains(team.id) { Image(systemName: "star.fill").font(.system(size: 9)).foregroundColor(green) }
        }.frame(height: 25)
    }
    private func score(_ value: String) -> some View {
        Text(match.state == "pre" || value.isEmpty ? "–" : value).frame(height: 25)
    }
    private var details: some View {
        VStack(alignment: .leading, spacing: 8) {
            Divider()
            if !match.venue.isEmpty { Text(match.venue).font(.system(size: 10)).foregroundColor(muted) }
            ForEach([("possessionPct", "控球率"), ("shotsSummary", "射门"), ("shotsOnTarget", "射正"), ("foulsCommitted", "犯规"), ("wonCorners", "角球")], id: \.0) { key, label in
                if match.homeStats[key] != nil || match.awayStats[key] != nil {
                    HStack {
                        Text(match.homeStats[key] ?? "—").frame(width: 60)
                        Spacer()
                        Text(label).foregroundColor(muted)
                        Spacer()
                        Text(match.awayStats[key] ?? "—").frame(width: 60)
                    }.font(.system(size: 11)).monospacedDigit()
                }
            }
            ForEach(Array(match.events.enumerated()), id: \.offset) { _, event in
                Text("\(event.clock)  \(event.kind) · \(event.player)").font(.system(size: 10))
            }
            if match.homeStats.isEmpty && match.awayStats.isEmpty && match.events.isEmpty {
                Text("数据源暂未提供详细比赛数据").font(.system(size: 10)).foregroundColor(muted)
            }
            HStack {
                followButton(match.home)
                Spacer()
                followButton(match.away)
            }
            if match.stale, let updated = match.updated {
                Text("缓存时间：\(Beijing.format(updated, "MM-dd HH:mm")) · 非实时比分").font(.system(size: 9)).foregroundColor(.orange)
            }
        }
    }
    private func followButton(_ team: Team) -> some View {
        Button(model.preferences.favorites.contains(team.id) ? "取消关注 \(model.name(team.name))" : "关注 \(model.name(team.name))") { model.follow(team.id) }
            .buttonStyle(CardButton(active: model.preferences.favorites.contains(team.id)))
    }
}

struct FavoritesView: View {
    @ObservedObject var model: AppModel
    @Environment(\.dismiss) private var dismiss
    @ViewState private var search = ""
    private var filtered: [Team] {
        model.teams.filter { search.isEmpty || model.name($0.name).localizedCaseInsensitiveContains(search) || $0.name.localizedCaseInsensitiveContains(search) }
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack { Text("关注球队").font(.title2.bold()); Spacer(); Button("完成") { dismiss() } }
            Text("首位球队显示在卡片标题和收纳条上。").font(.caption).foregroundColor(muted)
            if !model.preferences.favorites.isEmpty {
                ScrollView {
                    VStack(spacing: 7) {
                        ForEach(model.preferences.favorites, id: \.self) { id in
                            HStack {
                                Text(model.teams.first { $0.id == id }.map { model.name($0.name) } ?? id)
                                Spacer()
                                Button { model.moveFavorite(id, by: -1) } label: { Image(systemName: "arrow.up") }
                                    .disabled(model.preferences.favorites.first == id).help("向前排序")
                                Button { model.moveFavorite(id, by: 1) } label: { Image(systemName: "arrow.down") }
                                    .disabled(model.preferences.favorites.last == id).help("向后排序")
                                Button { model.follow(id) } label: { Image(systemName: "xmark") }.help("取消关注")
                            }.font(.system(size: 11))
                        }
                    }
                }.frame(maxHeight: 130)
            }
            TextField("搜索球队中文或英文名称", text: $search).textFieldStyle(.roundedBorder)
            ScrollView {
                LazyVStack(spacing: 4) {
                    ForEach(filtered) { team in
                        Button { model.follow(team.id) } label: {
                            HStack(spacing: 9) {
                                Badge(team: team, size: 22)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(model.name(team.name)).font(.system(size: 12))
                                    Text(team.name).font(.system(size: 9)).foregroundColor(muted)
                                }
                                Spacer()
                                Image(systemName: model.preferences.favorites.contains(team.id) ? "star.fill" : "star")
                                    .foregroundColor(model.preferences.favorites.contains(team.id) ? green : muted)
                            }.padding(6).contentShape(Rectangle())
                        }.buttonStyle(.plain)
                    }
                }
            }
        }.padding(20).frame(width: 350, height: 480).background(shell).foregroundColor(ink)
    }
}

struct SettingsView: View {
    @ObservedObject var model: AppModel
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack { Text("设置").font(.title2.bold()); Spacer(); Button("完成") { dismiss() } }
            Toggle("窗口置顶", isOn: $model.preferences.pinned)
            Toggle("启动时仅显示菜单栏图标", isOn: $model.preferences.startInMenuBar)
            Text("收纳或关闭卡片后暂停比分刷新。点击顶部图标重新展开完整卡片后同步；退出请使用菜单中的“退出看球”。")
                .font(.caption).foregroundColor(muted)
            Toggle("比分变化时播放提示音", isOn: $model.preferences.sound)
            VStack(alignment: .leading) {
                Text("卡片不透明度 \(Int(model.preferences.opacity * 100))%")
                Slider(value: $model.preferences.opacity, in: 0.1...1)
            }
            VStack(alignment: .leading) {
                Text("收纳条不透明度 \(Int(model.preferences.compactOpacity * 100))%")
                Slider(value: $model.preferences.compactOpacity, in: 0.1...1)
            }
            Divider()
            Toggle("登录 Mac 时自动启动", isOn: Binding(get: { model.loginEnabled }, set: model.setLogin))
            Text("默认关闭。请先把应用放入“应用程序”文件夹，再开启自启。").font(.caption).foregroundColor(muted)
            if !model.loginMessage.isEmpty { Text(model.loginMessage).font(.caption).foregroundColor(.orange) }
            Button("打开系统登录项设置") { SMAppService.openSystemSettingsLoginItems() }
            Divider()
            Button("打开本地设置文件夹") { NSWorkspace.shared.open(model.store.directory) }
            Text("看球 · Touchline for macOS\n完整卡片显示时：赛中每 30 秒、其余每 2 分钟更新。\n日期和时间统一按北京时间显示。").font(.caption).foregroundColor(muted)
        }.font(.system(size: 12)).padding(22).frame(width: 330).background(shell).foregroundColor(ink)
            .onAppear { model.updateLoginStatus() }
    }
}
