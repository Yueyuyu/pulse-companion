import CoreFoundation
import Foundation

typealias JSONObject = [String: Any]

enum ThreadIdentity {
    static func parse(_ raw: Any?) -> UUID? {
        guard let text = raw as? String, text.utf8.count == 36,
              let uuid = UUID(uuidString: text), uuid.uuidString.lowercased() == text.lowercased(),
              uuid.uuidString != "00000000-0000-0000-0000-000000000000" else { return nil }
        return uuid
    }

    static func deepLink(_ id: UUID) -> URL {
        // 不接收来自响应或 UI 的任意 URL，只用经过校验的 UUID 组装固定协议。
        URL(string: "codex://threads/" + id.uuidString.lowercased())!
    }
}

private func number(_ value: Any?) -> Double? {
    guard let value = value as? NSNumber, CFGetTypeID(value) != CFBooleanGetTypeID(),
          value.doubleValue.isFinite else { return nil }
    return value.doubleValue
}

struct WeeklyQuota: Equatable {
    let remaining: Int
    let reset: Date?

    static func parse(_ result: JSONObject, now: Date = Date()) -> WeeklyQuota? {
        var buckets: [(String, JSONObject)] = []
        if let keyed = result["rateLimitsByLimitId"] as? [String: JSONObject] {
            for key in keyed.keys.sorted() { if let bucket = keyed[key] { buckets.append((key, bucket)) } }
        }
        if let single = result["rateLimits"] as? JSONObject { buckets.append(("", single)) }
        var best: (rank: Int, quota: WeeklyQuota)?
        for (key, bucket) in buckets {
            let codex = ((bucket["limitId"] as? String) ?? key).lowercased() == "codex"
            for name in ["secondary", "primary"] {
                guard let window = bucket[name] as? JSONObject,
                      let duration = number(window["windowDurationMins"]), duration.rounded() == duration,
                      (8640...11520).contains(duration), let used = number(window["usedPercent"]),
                      (0...100).contains(used) else { continue }
                var reset: Date?
                if let raw = window["resetsAt"], !(raw is NSNull) {
                    guard let timestamp = number(raw), timestamp > now.timeIntervalSince1970,
                          timestamp < 253402300800 else { continue }
                    reset = Date(timeIntervalSince1970: timestamp)
                }
                let rank = Int(abs(duration - 10080)) * 4 + (codex ? 0 : 2) + (name == "secondary" ? 0 : 1)
                if best == nil || rank < best!.rank {
                    best = (rank, WeeklyQuota(remaining: Int((100 - used).rounded()), reset: reset))
                }
            }
        }
        return best?.quota
    }
}

enum ThreadState: String {
    case running = "执行中", attention = "需要处理", completed = "已完成"
    case stopped = "已中止", idle = "空闲", unknown = "状态未知"

    var priority: Int {
        switch self { case .attention: return 0; case .running: return 1; default: return 2 }
    }
}

struct LifecycleEvent {
    let state: ThreadState
    let date: Date
}

struct CompanionThread: Identifiable {
    let id: UUID
    let title: String
    let state: ThreadState
    let activity: Date

    static func fallbackTitle(_ id: UUID) -> String {
        "对话 · " + String(id.uuidString.suffix(8)).lowercased()
    }

    static func parseList(_ result: JSONObject, now: Date = Date(),
                          lifecycle: (UUID, String) -> LifecycleEvent? = { _, _ in nil }) -> [CompanionThread]? {
        guard let rows = result["data"] as? [JSONObject] else { return nil }
        var seen: Set<UUID> = []
        var threads: [CompanionThread] = []
        for row in rows.prefix(80) {
            guard let id = ThreadIdentity.parse(row["id"]), !seen.contains(id),
                  row["ephemeral"] as? Bool != true,
                  row["threadSource"] as? String != "memory_consolidation",
                  (row["source"] as? JSONObject)?["subAgent"] == nil else { continue }
            seen.insert(id)
            let updated = Date(timeIntervalSince1970: number(row["updatedAt"]) ?? 0)
            let status = row["status"] as? JSONObject
            let kind = status?["type"] as? String
            let flags = status?["activeFlags"] as? [String] ?? []
            var activity = updated
            let state: ThreadState
            if kind == "systemError" || (kind == "active" && (flags.contains("waitingOnApproval") || flags.contains("waitingOnUserInput"))) {
                state = .attention
            } else if kind == "active" {
                state = .running
            } else if now.timeIntervalSince(updated) <= 7 * 86400, let path = row["path"] as? String,
                      let event = lifecycle(id, path), event.date <= now.addingTimeInterval(60),
                      event.state != .running || now.timeIntervalSince(event.date) <= 12 * 3600 {
                state = event.state
                activity = event.date
            } else {
                // 独立 app-server 返回 notLoaded 很常见；空闲、未加载和被截断都不是完成证据。
                state = kind == "idle" ? .idle : .unknown
            }
            let name = (row["name"] as? String ?? "").split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
            threads.append(CompanionThread(id: id, title: name.isEmpty ? fallbackTitle(id) : String(name.prefix(80)),
                                           state: state, activity: activity))
        }
        return threads.sorted {
            if $0.state.priority != $1.state.priority { return $0.state.priority < $1.state.priority }
            if $0.activity != $1.activity { return $0.activity > $1.activity }
            return $0.id.uuidString < $1.id.uuidString
        }
    }
}

struct Favorites {
    static let limit = 5
    private(set) var ids: [UUID] = []

    init(stored: [String]) {
        for raw in stored {
            if let id = ThreadIdentity.parse(raw), !ids.contains(id) { ids.append(id) }
            if ids.count == Self.limit { break }
        }
    }

    mutating func toggle(_ id: UUID) -> Bool {
        if let index = ids.firstIndex(of: id) { ids.remove(at: index); return true }
        guard ids.count < Self.limit else { return false }
        ids.append(id)
        return true
    }
}

/// 有序列表和 UUID 索引属于同一份快照，供关注行和变化提醒共用。
struct ThreadFeed {
    private(set) var rows: [CompanionThread]?
    private var byID: [UUID: CompanionThread] = [:]
    subscript(id: UUID) -> CompanionThread? { byID[id] }

    mutating func reset() { rows = nil; byID.removeAll() }

    mutating func update(_ tasks: [CompanionThread]) -> [CompanionThread] {
        let hadBaseline = rows != nil, old = byID
        rows = tasks
        byID = Dictionary(uniqueKeysWithValues: tasks.map { ($0.id, $0) })
        guard hadBaseline else { return [] }
        return tasks.filter { task in
            guard let before = old[task.id], task.activity >= before.activity, before.state != task.state else { return false }
            return task.state == .attention || (before.state == .running && task.state == .completed)
        }
    }
}
