import AppKit
import Darwin
import UserNotifications

@MainActor
final class CompanionDelegate: NSObject, NSApplicationDelegate, UNUserNotificationCenterDelegate {
    private let dashboard = Dashboard()
    private var item: NSStatusItem?
    private var panel: NSPopover?

    func applicationWillFinishLaunching(_ notification: Notification) {
        UNUserNotificationCenter.current().delegate = self
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        self.item = item
        item.button?.image = CompanionPanel.statusIcon()
        item.button?.imagePosition = .imageLeading
        item.button?.toolTip = "Pulse Companion · Codex 额度与任务"
        item.button?.setAccessibilityLabel("Pulse Companion")
        item.button?.target = self
        item.button?.action = #selector(togglePanel)
        panel = CompanionPanel.make(dashboard)
        dashboard.onBadgeChange = { [weak self] percent in self?.item?.button?.title = percent.map { " " + String($0) + "%" } ?? "" }
        dashboard.start()
    }

    @objc private func togglePanel() {
        guard let panel, let button = item?.button else { return }
        if panel.isShown { panel.performClose(nil) }
        else {
            dashboard.checkSettings()
            panel.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
            panel.contentViewController?.view.window?.makeKey()
        }
    }

    func applicationWillTerminate(_ notification: Notification) { dashboard.stop() }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                              withCompletionHandler completion: @escaping (UNNotificationPresentationOptions) -> Void) {
        completion([.banner, .list])
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                             withCompletionHandler completion: @escaping () -> Void) {
        guard response.actionIdentifier == UNNotificationDefaultActionIdentifier,
              let id = ThreadIdentity.parse(response.notification.request.content.userInfo["threadID"]) else { completion(); return }
        DispatchQueue.main.async { [weak self] in self?.dashboard.open(id); completion() }
    }
}

@main
enum PulseCompanion {
    @MainActor static func main() {
        let arguments = Array(CommandLine.arguments.dropFirst())
        if arguments == ["--self-test"] { exit(OfflineBundleSmoke.run() ? 0 : 1) }
        guard arguments.isEmpty || (arguments.count == 1 && arguments[0].hasPrefix("-psn_")) else { exit(64) }
        guard !NSRunningApplication.runningApplications(withBundleIdentifier: "io.github.yueyuyu.pulse-companion")
            .contains(where: { $0.processIdentifier != getpid() && !$0.isTerminated }) else { return }
        // 只影响本进程；Codex 在写入期间退出时由连接层处理 EPIPE，不使菜单栏一起退出。
        signal(SIGPIPE, SIG_IGN)
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        let delegate = CompanionDelegate()
        app.delegate = delegate
        withExtendedLifetime(delegate) { app.run() }
    }
}

enum OfflineBundleSmoke {
    /// 从打包后的可执行文件调用：不启动监视器、不创建菜单栏项、不请求通知、不开对话、
    /// 不读真实偏好。真实面板布局在未展示的窗口渲染，不能替代真机交互或真实账户验收。
    @MainActor static func run() -> Bool {
        guard Bundle.main.bundleIdentifier == "io.github.yueyuyu.pulse-companion",
              let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
              !version.isEmpty, let icon = CompanionPanel.icon,
              let settings = UserDefaults(suiteName: "io.github.yueyuyu.pulse-companion.smoke." + UUID().uuidString) else {
            print("FAIL bundle metadata/icon"); return false
        }
        let originalSize = icon.size
        guard let badge = CompanionPanel.statusIcon(), badge !== icon, icon.size == originalSize,
              badge.size == NSSize(width: 23, height: 23) else { print("FAIL menu icon copy"); return false }
        _ = NSApplication.shared
        NSApp.setActivationPolicy(.prohibited)
        let model = Dashboard(defaults: settings)
        model.apply(.unavailable("未检测到 Codex 桌面端"))
        let panel = CompanionPanel.make(model)
        guard !panel.isShown, panel.behavior == .transient, let host = panel.contentViewController else {
            print("FAIL native popover"); return false
        }
        let rect = NSRect(origin: .zero, size: CompanionPanel.size)
        let window = NSWindow(contentRect: rect, styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.contentView = host.view
        host.view.frame = rect
        host.view.layoutSubtreeIfNeeded()
        guard !window.isVisible, model.feed.rows == nil, model.quota == nil, !model.connected,
              model.status == "未检测到 Codex 桌面端", host.view.bounds.size == CompanionPanel.size,
              let bitmap = host.view.bitmapImageRepForCachingDisplay(in: rect) else {
            print("FAIL offline native surface"); return false
        }
        host.view.cacheDisplay(in: rect, to: bitmap)
        // 无前台 WindowServer 合成的 CI 会话可能只返回空的 layer 缓存；仍须在该
        // 可执行文件内栅格化同一份真实面板，并检查有色像素，而不能只接受尺寸。
        if !hasInk(bitmap) {
            guard let alternate = CompanionPanel.offscreenBitmap(model), hasInk(alternate) else {
                print("FAIL blank native surface"); return false
            }
        }
        print("PASS mounted native bundle: version=" + version + " icon=ok popover=ok offscreen-render=ok account=not-tested")
        return true
    }

    @MainActor private static func hasInk(_ bitmap: NSBitmapImageRep) -> Bool {
        guard bitmap.pixelsWide >= 360, bitmap.pixelsHigh >= 462 else { return false }
        var colors = Set<Int>()
        for y in stride(from: 2, to: bitmap.pixelsHigh, by: 5) {
            for x in stride(from: 2, to: bitmap.pixelsWide, by: 5) {
                guard let c = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB), c.alphaComponent > 0.15 else { continue }
                colors.insert(Int(c.redComponent * 15) * 256 + Int(c.greenComponent * 15) * 16 + Int(c.blueComponent * 15))
                if colors.count >= 4 { break }
            }
            if colors.count >= 4 { break }
        }
        return colors.count >= 4
    }
}
