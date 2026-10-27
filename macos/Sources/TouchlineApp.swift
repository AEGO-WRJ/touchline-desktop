import AppKit
import SwiftUI
import Combine

final class CardWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSMenuDelegate {
    private var window: CardWindow!
    private var statusItem: NSStatusItem!
    private var lastCompact: Bool?
    private var applyingFrame = false
    private var model: AppModel!
    private var observation: AnyCancellable?
    private var badgeTask: Task<Void, Never>?
    private var badgeKey = ""
    private var primaryID: String?
    private var visibilityItem: NSMenuItem!
    private var startupItem: NSMenuItem!
    private var pinItem: NSMenuItem!
    private var refreshItem: NSMenuItem!
    private var summaryItem: NSMenuItem!

    func applicationDidFinishLaunching(_ notification: Notification) {
        // An isolated data directory is useful for development smoke checks.
        let testRoot = ProcessInfo.processInfo.environment["TOUCHLINE_DATA_DIR"].map { URL(fileURLWithPath: $0) }
        model = AppModel(dataDirectory: testRoot, cacheDirectory: testRoot?.appendingPathComponent("cache"))
        window = CardWindow(contentRect: NSRect(x: 0, y: 0, width: 420, height: 740), styleMask: [.borderless, .resizable], backing: .buffered, defer: false)
        window.title = "看球 · Touchline"
        window.isReleasedWhenClosed = false
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = true
        window.appearance = NSAppearance(named: .darkAqua)
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.contentView = NSHostingView(rootView: CardView(model: model, hide: { [weak self] in self?.hideCard() }).environmentObject(model))
        window.delegate = self
        model.presentationChanged = { [weak self] in self?.applyPresentation() }
        applyPresentation()
        let area = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let p = model.preferences
        window.setFrameOrigin(NSPoint(x: p.x ?? area.maxX - window.frame.width - 24, y: p.y ?? area.maxY - window.frame.height - 24))
        keepOnScreen()
        setupMenus()
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(woke), name: NSWorkspace.didWakeNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(willSleep), name: NSWorkspace.willSleepNotification, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        observation = model.objectWillChange.receive(on: RunLoop.main).sink { [weak self] in self?.updateStatusItem() }
        updateStatusItem()
        if !model.preferences.startInMenuBar { showCard() }
        else { model.footer = "已暂停刷新 · 展开完整卡片后同步" }
        model.begin()
    }
    private func setupMenus() {
        let appMenu = NSMenu()
        let appRoot = NSMenuItem()
        appRoot.submenu = appMenu
        let bar = NSMenu()
        bar.addItem(appRoot)
        appMenu.addItem(item("显示看球", #selector(showCard), "1"))
        appMenu.addItem(item("隐藏卡片", #selector(hideCard), "w"))
        appMenu.addItem(item("刷新比分", #selector(refresh), "r"))
        appMenu.addItem(item("设置…", #selector(settings), ","))
        appMenu.addItem(.separator())
        appMenu.addItem(item("退出看球", #selector(quit), "q"))
        let editRoot = NSMenuItem(title: "编辑", action: nil, keyEquivalent: "")
        let editMenu = NSMenu(title: "编辑")
        for (title, action, key) in [("剪切", "cut:", "x"), ("复制", "copy:", "c"), ("粘贴", "paste:", "v"), ("全选", "selectAll:", "a")] {
            editMenu.addItem(NSMenuItem(title: title, action: Selector(action), keyEquivalent: key))
        }
        editRoot.submenu = editMenu
        bar.addItem(editRoot)
        NSApp.mainMenu = bar
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "soccerball", accessibilityDescription: "看球")
        statusItem.button?.imagePosition = .imageLeading
        statusItem.button?.setAccessibilityLabel("看球 · 菜单栏")
        statusItem.button?.toolTip = "看球 · Touchline"
        let menu = NSMenu()
        menu.delegate = self
        menu.autoenablesItems = false
        summaryItem = NSMenuItem(title: "正在同步比分…", action: nil, keyEquivalent: "")
        summaryItem.isEnabled = false
        menu.addItem(summaryItem)
        menu.addItem(.separator())
        visibilityItem = item("显示卡片", #selector(toggleVisibility))
        menu.addItem(visibilityItem)
        menu.addItem(item("展开 / 收纳", #selector(toggleCompact)))
        pinItem = item("窗口置顶", #selector(togglePinned))
        menu.addItem(pinItem)
        menu.addItem(item("关注球队…", #selector(favorites)))
        refreshItem = item("打开卡片并刷新", #selector(refresh))
        menu.addItem(refreshItem)
        startupItem = item("启动时仅显示菜单栏图标", #selector(toggleStartup))
        menu.addItem(startupItem)
        menu.addItem(item("设置…", #selector(settings)))
        menu.addItem(.separator())
        menu.addItem(item("退出看球", #selector(quit)))
        statusItem.menu = menu
    }
    func menuWillOpen(_ menu: NSMenu) {
        visibilityItem.title = window.isVisible ? "隐藏卡片，暂停刷新" : "显示卡片"
        startupItem.state = model.preferences.startInMenuBar ? .on : .off
        pinItem.state = model.preferences.pinned ? .on : .off
        refreshItem.isEnabled = !model.loading
        summaryItem.title = !model.fullCardVisible ? "已暂停刷新 · 展开完整卡片后同步" : model.loading ? "正在同步比分…" : "当前赛程 \(model.liveCount) 场进行中 · \(model.footer)"
    }
    private func updateStatusItem() {
        guard let button = statusItem?.button else { return }
        button.title = model.fullCardVisible && model.liveCount > 0 ? " \(model.liveCount)" : ""
        let team = model.primaryTeam
        let title = team.map { model.name($0.name) } ?? "看球"
        button.toolTip = model.fullCardVisible ? "\(title) · 当前赛程 \(model.liveCount) 场进行中\n\(model.footer)\n点击显示菜单" : "\(title) · 已暂停比分刷新\n展开完整卡片后同步"
        let urls = model.badgeURLs(team)
        let key = urls.map(\.absoluteString).joined(separator: "|") + ":\(model.badgeRevision)"
        guard key != badgeKey else { return }
        badgeKey = key
        badgeTask?.cancel()
        if primaryID != team?.id || team == nil {
            button.image = NSImage(systemSymbolName: "soccerball", accessibilityDescription: "看球")
            primaryID = team?.id
        }
        badgeTask = Task { [weak self, badges = model.badges] in
            @MainActor func apply(_ data: Data?) {
                guard !Task.isCancelled, let self, self.badgeKey == key, let data, let image = NSImage(data: data) else { return }
                image.size = NSSize(width: 18, height: 18)
                image.isTemplate = false
                self.statusItem.button?.image = image
            }
            apply(await badges.cached(urls))
            apply(await badges.load(urls))
        }
    }
    private func item(_ title: String, _ action: Selector, _ key: String = "") -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self
        return item
    }
    private func applyPresentation() {
        guard window != nil else { return }
        let p = model.preferences
        window.level = p.pinned ? .floating : .normal
        window.alphaValue = p.compact ? p.compactOpacity : p.opacity
        syncRefreshVisibility()
        guard lastCompact != p.compact else { return }
        let wasInitialized = lastCompact != nil
        applyingFrame = true
        let topRight = NSPoint(x: window.frame.maxX, y: window.frame.maxY)
        window.minSize = p.compact ? NSSize(width: 240, height: 40) : NSSize(width: 400, height: 480)
        window.maxSize = p.compact ? NSSize(width: 240, height: 40) : NSSize(width: 640, height: 1100)
        let size = p.compact ? NSSize(width: 240, height: 40) : NSSize(width: p.width, height: p.height)
        window.setFrame(NSRect(x: topRight.x - size.width, y: topRight.y - size.height, width: size.width, height: size.height), display: true)
        lastCompact = p.compact
        keepOnScreen()
        applyingFrame = false
        if wasInitialized { saveFrame() }
    }
    private func keepOnScreen() {
        let frame = window.frame
        let screen = NSScreen.screens.first { $0.visibleFrame.intersects(frame) } ?? NSScreen.main
        guard let area = screen?.visibleFrame else { return }
        let width = min(frame.width, area.width), height = min(frame.height, area.height)
        let adjusted = NSRect(x: max(area.minX, min(frame.minX, area.maxX - width)), y: max(area.minY, min(frame.minY, area.maxY - height)), width: width, height: height)
        if frame != adjusted { window.setFrame(adjusted, display: true) }
    }
    private func saveFrame() {
        guard !applyingFrame, model != nil else { return }
        var p = model.preferences
        p.x = window.frame.minX
        p.y = window.frame.minY
        if !p.compact { p.width = window.frame.width; p.height = window.frame.height }
        model.preferences = p
    }
    func windowDidMove(_ notification: Notification) { saveFrame() }
    func windowDidResize(_ notification: Notification) { saveFrame() }
    func windowShouldClose(_ sender: NSWindow) -> Bool { hideCard(); return false }
    func windowDidMiniaturize(_ notification: Notification) { syncRefreshVisibility() }
    func windowDidDeminiaturize(_ notification: Notification) { syncRefreshVisibility() }
    func applicationDidHide(_ notification: Notification) { syncRefreshVisibility() }
    func applicationDidUnhide(_ notification: Notification) { syncRefreshVisibility() }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showCard(); return true }
    func applicationWillTerminate(_ notification: Notification) { badgeTask?.cancel(); observation?.cancel(); saveFrame(); model.stop() }
    private func syncRefreshVisibility(force: Bool = false) {
        model.setFullCardVisible(window.isVisible && !window.isMiniaturized && !NSApp.isHidden && !model.preferences.compact, force: force)
    }
    @objc private func showCard() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); syncRefreshVisibility() }
    @objc private func hideCard() { window.orderOut(nil); syncRefreshVisibility() }
    @objc private func toggleVisibility() { if window.isVisible { hideCard() } else { showCard() } }
    @objc private func toggleStartup() { model.preferences.startInMenuBar.toggle() }
    @objc private func togglePinned() { model.preferences.pinned.toggle() }
    @objc private func favorites() { model.preferences.compact = false; showCard(); model.showFavorites = true }
    @objc private func refresh() {
        // Set visibility first so manual refresh also obeys the expanded-card rule.
        window.orderOut(nil)
        model.preferences.compact = false
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        syncRefreshVisibility(force: true)
    }
    @objc private func toggleCompact() { model.preferences.compact.toggle(); showCard() }
    @objc private func settings() { model.preferences.compact = false; showCard(); model.showSettings = true }
    @objc private func quit() { NSApp.terminate(nil) }
    @objc private func willSleep() { model.setFullCardVisible(false) }
    @objc private func woke() { syncRefreshVisibility(); model.tick() }
    @objc private func screensChanged() { keepOnScreen() }
}

@main
struct TouchlineApp {
    @MainActor static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        app.run()
        withExtendedLifetime(delegate) {}
    }
}
