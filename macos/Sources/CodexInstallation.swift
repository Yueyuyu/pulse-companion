import AppKit
import Security

enum CodexTarget: Equatable {
    case paused, missing, closed(URL), running(URL, pid_t)

    var bundleURL: URL? {
        switch self { case .closed(let url), .running(let url, _): return url; default: return nil }
    }
}

enum CodexInstallation {
    static let identifier = "com.openai.codex"
    private static let publisher = #"anchor apple generic and certificate leaf[subject.OU] = "2DC432GLL2""#

    @MainActor static func detect() -> CodexTarget {
        // 较新的桌面端名为 ChatGPT.app，但仍使用同一 Codex 标识和 URL scheme。
        if let app = NSRunningApplication.runningApplications(withBundleIdentifier: identifier)
            .first(where: { !$0.isTerminated && $0.bundleURL != nil }), let location = app.bundleURL {
            return .running(location.standardizedFileURL.resolvingSymlinksInPath(), app.processIdentifier)
        }
        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: identifier) {
            return .closed(url.standardizedFileURL.resolvingSymlinksInPath())
        }
        return .missing
    }

    private static func signed(_ url: URL, requirement: String) -> Bool {
        let flags = SecCSFlags(rawValue: 0)
        var code: SecStaticCode?
        var rule: SecRequirement?
        guard SecStaticCodeCreateWithPath(url as CFURL, flags, &code) == errSecSuccess, let code,
              SecRequirementCreateWithString(requirement as CFString, flags, &rule) == errSecSuccess, let rule else { return false }
        return SecStaticCodeCheckValidity(code, flags, rule) == errSecSuccess
    }

    static func trustedHost(_ app: URL) -> Bool {
        signed(app, requirement: publisher + #" and identifier "com.openai.codex""#)
    }

    static func executable(in app: URL) -> URL? {
        guard trustedHost(app) else { return nil }
        // 新版官方 bin/codex 是 shell 转发脚本。直接执行它所指向的已签名原生二进制；
        // 老版使用 Resources/codex。两者均不搜索 PATH，不读取用户指定的命令。
        let paths = ["Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex", "Contents/Resources/codex"]
        for path in paths {
            let file = app.appendingPathComponent(path).standardizedFileURL.resolvingSymlinksInPath()
            if file.path.hasPrefix(app.path + "/Contents/Resources/"),
               FileManager.default.isExecutableFile(atPath: file.path), signed(file, requirement: publisher) {
                return file
            }
        }
        return nil
    }
}

enum MonitorEvent {
    case unavailable(String), connected, quota(WeeklyQuota?), threads([CompanionThread]?)
}

final class ReadOnlyMonitor {
    private let queue = DispatchQueue(label: "io.github.yueyuyu.pulse-companion.codex", qos: .utility)
    private let emit: @MainActor (MonitorEvent) -> Void
    private let desiredLock = NSLock()
    private var desired: CodexTarget?
    private var target: CodexTarget?
    private var nextAttempt = Date.distantPast
    private lazy var rpc = CodexRPC(queue: queue, sessions: FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent(".codex/sessions", isDirectory: true)) { [weak self] event in
        guard let self else { return }
        switch event {
        case .ready: self.publish(.connected)
        case .quota(let quota): self.publish(.quota(quota))
        case .threads(let threads): self.publish(.threads(threads))
        case .disconnected:
            self.nextAttempt = Date().addingTimeInterval(15)
            self.publish(.unavailable("Codex 连接中断，稍后重试"))
        }
    }

    init(emit: @escaping @MainActor (MonitorEvent) -> Void) { self.emit = emit }

    private func want(_ target: CodexTarget?) {
        desiredLock.lock(); desired = target; desiredLock.unlock()
    }

    private func stillWanted(_ candidate: CodexTarget?) -> Bool {
        desiredLock.lock(); defer { desiredLock.unlock() }
        return desired == candidate
    }

    private func publish(_ event: MonitorEvent) {
        let candidate = target
        DispatchQueue.main.async { if self.stillWanted(candidate) { self.emit(event) } }
    }

    func tick(_ observed: CodexTarget, force: Bool = false) {
        want(observed)
        queue.async {
            guard self.stillWanted(observed) else { return }
            if self.target != observed {
                self.rpc.stop()
                self.target = observed
                self.nextAttempt = .distantPast
                switch observed {
                case .paused: self.publish(.unavailable("已暂停读取"))
                case .missing: self.publish(.unavailable("未检测到 Codex 桌面端"))
                case .closed: self.publish(.unavailable("打开 Codex 桌面端后自动连接"))
                case .running: break
                }
            }
            guard case .running(let app, _) = observed else { return }
            if self.rpc.isOpen { self.rpc.poll(force: force); return }
            guard force || Date() >= self.nextAttempt else { return }
            self.publish(.unavailable("正在连接 Codex…"))
            self.nextAttempt = Date().addingTimeInterval(30)
            // 更新器可能原位替换文件而不改变宿主 PID。每次实际启动都验当前包和 CLI，
            // 已建立的连接轮询不重新验签；URL 或上一次成功不是信任凭据。
            let executable = CodexInstallation.executable(in: app)
            guard self.stillWanted(observed) else { return }
            guard let binary = executable else {
                self.publish(.unavailable("未找到可验证的官方 Codex 组件"))
                return
            }
            do { try self.rpc.start(executable: binary) }
            catch { self.publish(.unavailable("无法启动 Codex 的只读连接")) }
        }
    }

    func stop() { want(nil); queue.sync { rpc.stop() } }

    func open(_ id: UUID, in app: URL, completion: @escaping @MainActor (Bool) -> Void) {
        queue.async {
            let verified = CodexInstallation.trustedHost(app)
            DispatchQueue.main.async {
                guard verified else { completion(false); return }
                let options = NSWorkspace.OpenConfiguration()
                options.activates = true
                NSWorkspace.shared.open([ThreadIdentity.deepLink(id)], withApplicationAt: app, configuration: options) { _, error in
                    DispatchQueue.main.async { completion(error == nil) }
                }
            }
        }
    }
}
