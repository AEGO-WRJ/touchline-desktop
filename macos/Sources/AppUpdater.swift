import AppKit
import Combine
import Sparkle

/// Sparkle owns scheduling, preferences, signature verification and installation.
/// This lifecycle is independent of the visible-card-only score refresh timer.
@MainActor
final class AppUpdater: NSObject, ObservableObject, SPUStandardUserDriverDelegate {
    private var controller: SPUStandardUpdaterController!
    @Published private(set) var canCheck = false
    @Published private(set) var automaticChecks = false
    @Published private(set) var automaticDownloads = false
    @Published private(set) var pendingVersion: String?
    var willPresent: (() -> Void)?

    override init() {
        super.init()
        controller = SPUStandardUpdaterController(startingUpdater: false, updaterDelegate: nil, userDriverDelegate: self)
        let updater = controller.updater
        updater.publisher(for: \.canCheckForUpdates).assign(to: &$canCheck)
        updater.publisher(for: \.automaticallyChecksForUpdates).assign(to: &$automaticChecks)
        updater.publisher(for: \.automaticallyDownloadsUpdates).assign(to: &$automaticDownloads)
    }

    func start() { controller.startUpdater() }
    func check() {
        guard canCheck else { return }
        willPresent?()
        NSApp.activate(ignoringOtherApps: true)
        controller.checkForUpdates(nil)
    }
    func setAutomaticChecks(_ enabled: Bool) { controller.updater.automaticallyChecksForUpdates = enabled }
    func setAutomaticDownloads(_ enabled: Bool) { controller.updater.automaticallyDownloadsUpdates = enabled }

    var menuTitle: String {
        pendingVersion.map { "发现新版 \($0)，查看更新…" } ?? "检查更新…"
    }
    var version: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "—" }

    // A menu-bar app should not steal focus when a scheduled update is found.
    nonisolated var supportsGentleScheduledUpdateReminders: Bool { true }
    nonisolated func standardUserDriverShouldHandleShowingScheduledUpdate(_ update: SUAppcastItem, andInImmediateFocus immediateFocus: Bool) -> Bool { false }
    nonisolated func standardUserDriverWillHandleShowingUpdate(_ handleShowingUpdate: Bool, forUpdate update: SUAppcastItem, state: SPUUserUpdateState) {
        let version = update.displayVersionString
        Task { @MainActor [weak self] in self?.pendingVersion = version }
    }
    nonisolated func standardUserDriverWillFinishUpdateSession() {
        Task { @MainActor [weak self] in self?.pendingVersion = nil }
    }
}
