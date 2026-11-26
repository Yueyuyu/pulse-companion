import AppKit
import Combine
import ServiceManagement
import UserNotifications

@MainActor
final class Dashboard: ObservableObject {
    @Published private(set) var status = "正在检测 Codex…"
    @Published private(set) var connected = false
    @Published private(set) var quota: WeeklyQuota?
    @Published private(set) var feed = ThreadFeed()
    @Published private(set) var favorites: Favorites
    @Published private(set) var paused: Bool
    @Published private(set) var alerts: Bool
    @Published private(set) var loginStatus: SMAppService.Status = .notRegistered
    @Published private(set) var note: String?
    private let defaults: UserDefaults
    private lazy var center = UNUserNotificationCenter.current()
    private var timer: Timer?
    private lazy var monitor = ReadOnlyMonitor { [weak self] event in self?.apply(event) }
    var onBadgeChange: ((Int?) -> Void)?

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        favorites = Favorites(stored: defaults.stringArray(forKey: "favoriteThreadIDs") ?? [])
        paused = defaults.bool(forKey: "paused")
        alerts = defaults.bool(forKey: "taskAlerts")
    }

    func start() {
        checkSettings()
        tick()
        let clock = Timer(timeInterval: 2, repeats: true) { [weak self] _ in
            DispatchQueue.main.async { self?.tick() }
        }
        RunLoop.main.add(clock, forMode: .common)
        timer = clock
    }
    func stop() { timer?.invalidate(); monitor.stop() }
    func tick(force: Bool = false) {
        if let reset = quota?.reset, reset <= Date() { quota = nil; onBadgeChange?(nil) }
        monitor.tick(paused ? .paused : CodexInstallation.detect(), force: force)
    }

    func apply(_ event: MonitorEvent) {
        if paused {
            clear("已暂停读取")
            return
        }
        switch event {
        case .unavailable(let message): clear(message)
        case .connected: connected = true; status = "Codex 已连接 · 只读"
        case .quota(let value): quota = value; onBadgeChange?(value?.remaining)
        case .threads(let value):
            guard let value else { feed.reset(); return }
            let changed = feed.update(value)
            if alerts { for task in changed { notify(task) } }
        }
    }

    private func clear(_ message: String) {
        status = message; connected = false; quota = nil; feed.reset()
        onBadgeChange?(nil)
    }

    func togglePause() {
        paused.toggle()
        defaults.set(paused, forKey: "paused")
        note = nil
        if paused { clear("已暂停读取") }
        tick(force: true)
    }

    func toggleFavorite(_ id: UUID) {
        var updated = favorites
        guard updated.toggle(id) else { note = "最多关注 \(Favorites.limit) 项；可先取消一项。"; return }
        favorites = updated
        // 不存标题或正文；离线时保留按选择顺序排列的 UUID。
        defaults.set(updated.ids.map { $0.uuidString.lowercased() }, forKey: "favoriteThreadIDs")
        note = nil
    }

    func open(_ id: UUID) {
        guard let app = CodexInstallation.detect().bundleURL else { note = "没有找到可打开该对话的 Codex 桌面端。"; return }
        monitor.open(id, in: app) { [weak self] ok in
            self?.note = ok ? nil : "无法打开对话；请核对官方 Codex 桌面端的安装。"
        }
    }

    func checkSettings() {
        loginStatus = SMAppService.mainApp.status
        guard alerts else { return }
        center.getNotificationSettings { [weak self] settings in
            if settings.authorizationStatus == .denied {
                DispatchQueue.main.async { self?.setAlerts(false); self?.note = "系统通知未获允许。" }
            }
        }
    }

    func toggleAlerts() {
        if alerts { setAlerts(false); note = nil; return }
        center.requestAuthorization(options: [.alert]) { [weak self] allowed, _ in
            DispatchQueue.main.async {
                self?.setAlerts(allowed)
                self?.note = allowed ? "将在任务完成或需要处理时提醒。" : "未获通知权限；可在系统设置→通知中更改。"
            }
        }
    }

    private func setAlerts(_ value: Bool) {
        alerts = value
        defaults.set(value, forKey: "taskAlerts")
    }

    private func notify(_ task: CompanionThread) {
        let content = UNMutableNotificationContent()
        content.title = task.state == .attention ? "Codex 需要你处理" : "Codex 任务已完成"
        content.body = task.title
        content.userInfo = ["threadID": task.id.uuidString.lowercased()]
        center.add(UNNotificationRequest(identifier: "thread-" + task.id.uuidString, content: content, trigger: nil)) { [weak self] error in
            if error != nil { DispatchQueue.main.async { self?.note = "系统未能投递通知；可检查通知设置。" } }
        }
    }

    func toggleLogin() {
        let service = SMAppService.mainApp
        do {
            if service.status == .enabled { try service.unregister() }
            else if service.status == .requiresApproval { SMAppService.openSystemSettingsLoginItems() }
            else {
                let location = Bundle.main.bundleURL.standardizedFileURL.path
                let userApps = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications").path
                guard location.hasPrefix("/Applications/") || location.hasPrefix(userApps + "/") else {
                    note = "先将 Pulse Companion 拖到「应用程序」，再启用登录启动。"
                    return
                }
                try service.register()
            }
            loginStatus = service.status
            note = loginStatus == .requiresApproval ? "请在系统设置的登录项中批准。" : nil
        } catch { note = "无法更改登录启动；请检查系统设置中的登录项。" }
    }
}
