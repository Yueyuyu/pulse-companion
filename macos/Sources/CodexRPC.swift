import Foundation
#if canImport(Darwin)
import Darwin
#else
import Glibc
#endif

enum RPCEvent {
    case ready, disconnected, quota(WeeklyQuota?), threads([CompanionThread]?)
}

/// 所有可变状态归同一队列。没有通用 RPC 入口，不能创建/恢复线程、批准请求或发送模型输入。
final class CodexRPC {
    private enum Request { case initialize, quota, threads }
    private let queue: DispatchQueue
    private let events: (RPCEvent) -> Void
    private let lifecycle: CodexLifecycle
    private var child: Process?
    private var input: FileHandle?
    private var output: FileHandle?
    private var generation = 0
    private var incoming = Data()
    private var initialized = false
    private var nextID = 0
    private var pending: [Int: (Request, Date)] = [:]
    private var lastQuota = Date.distantPast
    private var lastThreads = Date.distantPast
    var isOpen: Bool { child != nil }

    init(queue: DispatchQueue, sessions: URL, events: @escaping (RPCEvent) -> Void) {
        self.queue = queue
        self.lifecycle = CodexLifecycle(sessions: sessions)
        self.events = events
    }

    func start(executable: URL) throws {
        stop()
        let token = generation
        let process = Process(), stdin = Pipe(), stdout = Pipe()
        process.executableURL = executable
        process.arguments = ["app-server"]
        process.currentDirectoryURL = URL(fileURLWithPath: "/", isDirectory: true)
        // GUI 环境可能带有其他工具的 API 密钥；子进程只使用官方 Codex 已有的本机登录。
        process.environment = ["HOME": NSHomeDirectory(), "PATH": "/usr/bin:/bin:/usr/sbin:/sbin", "LANG": "en_US.UTF-8"]
        process.standardInput = stdin
        process.standardOutput = stdout
        process.standardError = FileHandle.nullDevice
        let reader = stdout.fileHandleForReading
        reader.readabilityHandler = { [weak self] handle in
            let chunk = handle.availableData
            self?.queue.async { [weak self] in
                guard let self, self.generation == token else { return }
                if chunk.isEmpty { self.disconnect(); return }
                self.consume(chunk)
            }
        }
        process.terminationHandler = { [weak self] _ in
            self?.queue.async { [weak self] in
                guard let self, self.generation == token else { return }
                self.disconnect()
            }
        }
        child = process
        input = stdin.fileHandleForWriting
        output = reader
        do { try process.run() } catch { stop(); throw error }
        send(.initialize)
    }

    func stop() {
        generation &+= 1
        output?.readabilityHandler = nil
        try? input?.close()
        let old = child
        if let old, old.isRunning {
            old.terminate()
            queue.asyncAfter(deadline: .now() + 2) {
                if old.isRunning { _ = kill(old.processIdentifier, SIGKILL) }
            }
        }
        try? output?.close()
        child = nil; input = nil; output = nil
        incoming = Data(); initialized = false; nextID = 0
        pending.removeAll(); lastQuota = .distantPast; lastThreads = .distantPast
        lifecycle.clear()
    }

    func poll(force: Bool = false, now: Date = Date()) {
        guard isOpen else { return }
        if pending.values.contains(where: { now.timeIntervalSince($0.1) > 20 }) { disconnect(); return }
        if force { lastThreads = .distantPast; lastQuota = .distantPast }
        guard initialized, pending.isEmpty else { return }
        let quotaDue = now.timeIntervalSince(lastQuota) >= 60
        // 与现有宿主一致：先取任务，再取首份额度；不让两次请求互相占用服务器。
        if lastThreads == .distantPast || (!quotaDue && now.timeIntervalSince(lastThreads) >= 4) {
            lastThreads = now; send(.threads)
        } else if quotaDue {
            lastQuota = now; send(.quota)
        }
    }

    private func disconnect() {
        guard isOpen else { return }
        stop()
        events(.disconnected)
    }

    private func send(_ request: Request) {
        nextID += 1
        var message: JSONObject = ["id": nextID]
        switch request {
        case .initialize:
            message["method"] = "initialize"
            message["params"] = ["clientInfo": ["name": "pulse_companion", "title": "Pulse Companion",
                                                 "version": Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1"],
                                 "capabilities": ["experimentalApi": false]] as JSONObject
        case .quota:
            message["method"] = "account/rateLimits/read"
        case .threads:
            message["method"] = "thread/list"
            message["params"] = ["archived": false, "limit": 80, "sortKey": "updated_at", "sortDirection": "desc",
                                 "useStateDbOnly": true] as JSONObject
        }
        pending[nextID] = (request, Date())
        write(message)
    }

    private func write(_ object: JSONObject) {
        guard let input else { return }
        do {
            var data = try JSONSerialization.data(withJSONObject: object)
            data.append(10)
            try input.write(contentsOf: data)
        } catch { disconnect() }
    }

    private func consume(_ chunk: Data) {
        incoming.append(chunk)
        let maxFrame = 8 * 1024 * 1024
        while let end = incoming.firstIndex(of: 10) {
            guard incoming.distance(from: incoming.startIndex, to: end) <= maxFrame else { disconnect(); return }
            let line = incoming.prefix(upTo: end)
            let message = (try? JSONSerialization.jsonObject(with: line)) as? JSONObject
            incoming.removeSubrange(...end)
            // 只处理本会话自己发出的请求响应；服务端主动请求（含审批）从不应答。
            guard let message, message["method"] == nil, let id = message["id"] as? Int,
                  let (request, _) = pending.removeValue(forKey: id) else { continue }
            let result = message["error"] == nil ? message["result"] as? JSONObject : nil
            switch request {
            case .initialize:
                guard result != nil else { disconnect(); return }
                write(["method": "initialized", "params": JSONObject()])
                guard isOpen else { return }
                initialized = true
                events(.ready)
            case .quota:
                events(.quota(result.flatMap { WeeklyQuota.parse($0) }))
            case .threads:
                events(.threads(result.flatMap { CompanionThread.parseList($0, lifecycle: lifecycle.event) }))
            }
            if !isOpen { return }
            poll()
        }
        if incoming.count > maxFrame { disconnect() }
    }
}
