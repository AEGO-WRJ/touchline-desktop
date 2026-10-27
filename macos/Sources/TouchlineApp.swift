import AppKit
import SwiftUI

final class CardWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private var window: CardWindow!
    private var statusItem: NSStatusItem!
    private var lastCompact: Bool?
    private var applyingFrame = false
    private var model: AppModel!

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
        window.contentView = NSHostingView(rootView: CardView(model: model, hide: { [weak self] in self?.window.orderOut(nil) }))
        window.delegate = self
        model.presentationChanged = { [weak self] in self?.applyPresentation() }
        applyPresentation()
        let area = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let p = model.preferences
        window.setFrameOrigin(NSPoint(x: p.x ?? area.maxX - window.frame.width - 24, y: p.y ?? area.maxY - window.frame.height - 24))
        keepOnScreen()
        setupMenus()
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(woke), name: NSWorkspace.didWakeNotification, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        showCard()
        model.begin()
    }
    private func setupMenus() {
        let appMenu = NSMenu()
        let appRoot = NSMenuItem()
        appRoot.submenu = appMenu
        let bar = NSMenu()
        bar.addItem(appRoot)
        appMenu.addItem(item("显示看球", #selector(showCard), "1"))
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
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "soccerball", accessibilityDescription: "看球")
        statusItem.button?.toolTip = "看球 · Touchline"
        let menu = NSMenu()
        menu.addItem(item("显示看球", #selector(showCard)))
        menu.addItem(item("展开 / 收纳", #selector(toggleCompact)))
        menu.addItem(item("刷新比分", #selector(refresh)))
        menu.addItem(item("设置…", #selector(settings)))
        menu.addItem(.separator())
        menu.addItem(item("退出看球", #selector(quit)))
        statusItem.menu = menu
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
    func windowShouldClose(_ sender: NSWindow) -> Bool { sender.orderOut(nil); return false }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showCard(); return true }
    func applicationWillTerminate(_ notification: Notification) { saveFrame(); model.stop() }
    @objc private func showCard() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    @objc private func refresh() { model.refresh(force: true) }
    @objc private func toggleCompact() { model.preferences.compact.toggle(); showCard() }
    @objc private func settings() { model.preferences.compact = false; showCard(); model.showSettings = true }
    @objc private func quit() { NSApp.terminate(nil) }
    @objc private func woke() { model.tick(); model.refresh() }
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
