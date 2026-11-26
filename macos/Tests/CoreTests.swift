import Foundation
#if canImport(Darwin)
import Darwin
#else
import Glibc
#endif

private struct CheckFailure: Error, CustomStringConvertible { let description: String }
private func check(_ condition: @autoclosure () -> Bool, _ message: String) throws {
    if !condition() { throw CheckFailure(description: message) }
}

@main
enum CoreTests {
    static let id = UUID(uuidString: "10000000-0000-4000-8000-000000000001")!
    static let now = Date(timeIntervalSince1970: 1700000000)
    static func main() {
        signal(SIGPIPE, SIG_IGN)
        do {
            guard CommandLine.arguments.count == 2 else { throw CheckFailure(description: "missing fixture executable") }
            try quotaTests(); try threadTests(); try favoritesTests(); try lifecycleTests()
            try transportTest(executable: URL(fileURLWithPath: CommandLine.arguments[1]))
            print("PASS weekly fields, missing/stale limits, task states, transitions, five favorites, scoped rollout, real stdio transport")
        } catch { print("FAIL: " + String(describing: error)); exit(1) }
    }

    static func quotaTests() throws {
        let weekly: JSONObject = ["usedPercent": 32.5, "windowDurationMins": 10080, "resetsAt": 1700000060]
        let short: JSONObject = ["usedPercent": 0, "windowDurationMins": 300]
        let extra: JSONObject = ["usedPercent": 90, "windowDurationMins": 10080]
        let result: JSONObject = ["rateLimitsByLimitId": ["extras": ["primary": extra], "codex": ["primary": short, "secondary": weekly]]]
        let quota = WeeklyQuota.parse(result, now: now)
        try check(quota?.remaining == 68 && quota?.reset == now.addingTimeInterval(60), "weekly pool / reset")
        for invalid: JSONObject in [short, ["windowDurationMins": 10080], ["usedPercent": true, "windowDurationMins": 10080],
                                    ["usedPercent": 101, "windowDurationMins": 10080], ["usedPercent": 5, "windowDurationMins": 10080, "resetsAt": 2]] {
            try check(WeeklyQuota.parse(["rateLimits": ["primary": invalid]], now: now) == nil, "invalid windows must not be 100%")
        }
    }

    static func threadTests() throws {
        func task(_ type: String, flags: [String] = [], observed: LifecycleEvent? = nil) -> CompanionThread? {
            let row: JSONObject = ["id": id.uuidString, "name": "  状态  测试 ", "updatedAt": now.timeIntervalSince1970,
                                   "status": ["type": type, "activeFlags": flags] as JSONObject, "path": "fixture"]
            return CompanionThread.parseList(["data": [row]], now: now, lifecycle: { _, _ in observed })?.first
        }
        guard let running = task("active"), let attention = task("active", flags: ["waitingOnApproval"]),
              let completed = task("notLoaded", observed: LifecycleEvent(state: .completed, date: now)),
              let unknown = task("notLoaded"), let idle = task("idle") else { throw CheckFailure(description: "valid thread") }
        try check(running.state == .running && running.title == "状态 测试", "runtime active")
        try check(attention.state == .attention && task("systemError")?.state == .attention, "both attention sources")
        try check(completed.state == .completed && unknown.state == .unknown && idle.state == .idle, "idle/notLoaded are not completed")
        try check(task("notLoaded", observed: LifecycleEvent(state: .running, date: now.addingTimeInterval(-86400)))?.state == .unknown,
                  "stale start is unknown")
        let noThread: JSONObject = ["id": "../not-an-id", "name": "不应显示"]
        let ephemeral: JSONObject = ["id": id.uuidString, "ephemeral": true]
        try check(CompanionThread.parseList(["data": [noThread, ephemeral]])?.isEmpty == true, "invalid or ephemeral is filtered")
        try check(CompanionThread.parseList(["unexpected": []]) == nil, "missing response is not an empty task list")
        try check(ThreadIdentity.parse("codex://threads/" + id.uuidString) == nil, "only UUID input")
        try check(ThreadIdentity.deepLink(id).absoluteString == "codex://threads/" + id.uuidString.lowercased(), "safe deep link")
        var changes = ThreadFeed()
        try check(changes.update([running]).isEmpty, "baseline is silent")
        try check(changes.rows?.first?.id == id && changes[id]?.state == .running, "list and watched lookup share a snapshot")
        try check(changes.update([attention]).count == 1 && changes.update([attention]).isEmpty, "notify attention once")
        _ = changes.update([running])
        try check(changes.update([completed]).count == 1, "confirmed completion")
        _ = changes.update([running])
        try check(changes.update([]).isEmpty && changes.update([completed]).isEmpty, "absence is not completion")
        changes.reset()
        try check(changes.rows == nil && changes[id] == nil, "disconnect clears list and lookup together")
        try check(changes.update([attention]).isEmpty, "reconnect baseline is silent")
    }

    static func favoritesTests() throws {
        let ids = (0..<7).map { _ in UUID() }
        var favorites = Favorites(stored: ["invalid", ids[0].uuidString] + ids.map { $0.uuidString })
        try check(favorites.ids == Array(ids.prefix(5)), "ordered deduplicated capped restore")
        try check(!favorites.toggle(ids[5]), "sixth rejected")
        try check(favorites.toggle(ids[2]) && favorites.toggle(ids[5]), "remove then add")
        let restored = Favorites(stored: favorites.ids.map { $0.uuidString.lowercased() })
        try check(restored.ids == [ids[0],ids[1],ids[3],ids[4],ids[5]], "round-trip order")
    }

    static func lifecycleTests() throws {
        let fm = FileManager.default
        let scratch = fm.temporaryDirectory.appendingPathComponent("pulse-mac-tests-" + UUID().uuidString, isDirectory: true)
        let root = scratch.appendingPathComponent("sessions")
        let day = root.appendingPathComponent("2025/01/02")
        try fm.createDirectory(at: day, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: scratch) }
        let reader = CodexLifecycle(sessions: root)
        let filename = "rollout-2025-01-02T00-00-00-" + id.uuidString.lowercased() + ".jsonl"
        let file = day.appendingPathComponent(filename)
        func record(_ type: String, _ kind: String, extra: JSONObject = [:]) throws -> Data {
            var payload = extra; payload["type"] = kind
            var line = try JSONSerialization.data(withJSONObject: ["type": type, "timestamp": "2025-01-02T00:00:00.000Z", "payload": payload] as JSONObject)
            line.append(10)
            return line
        }
        var log = try record("event_msg", "task_started")
        log.append(try record("response_item", "task_complete"))
        try log.write(to: file)
        try check(reader.event(id: id, path: file.path)?.state == .running, "ignore lookalike message content")
        log.append(try record("event_msg", "task_complete"))
        try log.write(to: file)
        try check(reader.event(id: id, path: file.path)?.state == .completed, "confirmed completion marker")
        log.append(try record("event_msg", "task_complete", extra: ["error": ["code": "fixture"]]))
        try log.write(to: file)
        try check(reader.event(id: id, path: file.path)?.state == .attention, "structured failure")
        log.append(try record("event_msg", "turn_aborted"))
        try log.write(to: file)
        try check(reader.event(id: id, path: file.path)?.state == .stopped, "cancel is not completion")
        let external = scratch.appendingPathComponent(filename)
        try fm.moveItem(at: file, to: external)
        try fm.createSymbolicLink(at: file, withDestinationURL: external)
        try check(reader.event(id: id, path: file.path) == nil, "reject an escaping symlink")
        try check(reader.event(id: UUID(), path: external.path) == nil, "reject outside root")
    }

    static func transportTest(executable: URL) throws {
        let queue = DispatchQueue(label: "pulse.offline.protocol")
        let first = DispatchSemaphore(value: 0), second = DispatchSemaphore(value: 0)
        var gotQuota = false, gotActive = false, firstDone = false, gotAttention = false
        let rpc = CodexRPC(queue: queue, sessions: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)) { event in
            switch event {
            case .quota(let result): gotQuota = result?.remaining == 63 && result?.reset != nil
            case .threads(let tasks):
                if tasks?.first?.state == .running { gotActive = true }
                if tasks?.first?.state == .attention { gotAttention = true; second.signal() }
            default: break
            }
            if gotActive && gotQuota && !firstDone { firstDone = true; first.signal() }
        }
        try queue.sync { try rpc.start(executable: executable) }
        defer { queue.sync { rpc.stop() } }
        try check(first.wait(timeout: .now() + 10) == .success, "child handshake + two real stdio responses")
        queue.sync { rpc.poll(force: true) }
        try check(second.wait(timeout: .now() + 10) == .success && queue.sync { gotAttention }, "second live RPC changed status")
    }
}
