import Foundation

/// 独立的 app-server 不一定加载桌面线程。仅按 thread/list 给出的 UUID 和路径
/// 读取 Codex 自己的 rollout 尾部；不遍历目录、不解析/保存消息、工具内容或账户文件。
final class CodexLifecycle {
    private struct Entry {
        let path: String
        let size: Int
        let modified: Date
        let event: LifecycleEvent?
    }
    private let root: URL
    private let byteLimit = 512 * 1024
    private var cache: [UUID: Entry] = [:]
    private let iso = ISO8601DateFormatter()
    private let fractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    init(sessions: URL) { root = sessions.standardizedFileURL.resolvingSymlinksInPath() }
    func clear() { cache.removeAll() }

    func event(id: UUID, path: String) -> LifecycleEvent? {
        guard path.hasPrefix("/"), path.utf8.count < 4096 else { return nil }
        let file = URL(fileURLWithPath: path).standardizedFileURL.resolvingSymlinksInPath()
        let prefix = root.path + "/"
        guard file.path.hasPrefix(prefix) else { return nil }
        let pieces = file.path.dropFirst(prefix.count).split(separator: "/")
        guard pieces.count == 4, pieces[0].count == 4, pieces[1].count == 2, pieces[2].count == 2,
              pieces.prefix(3).allSatisfy({ $0.utf8.allSatisfy({ (48...57).contains($0) }) }),
              file.lastPathComponent.hasPrefix("rollout-"),
              file.lastPathComponent.lowercased().hasSuffix("-" + id.uuidString.lowercased() + ".jsonl"),
              let props = try? file.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey]),
              props.isRegularFile == true, let size = props.fileSize, size > 0,
              let modified = props.contentModificationDate else { return nil }
        let prior = cache[id]
        if let prior, prior.path == file.path, prior.size == size, prior.modified == modified { return prior.event }
        guard let handle = try? FileHandle(forReadingFrom: file) else { return nil }
        defer { try? handle.close() }
        do {
            let start = max(0, size - byteLimit)
            try handle.seek(toOffset: UInt64(start))
            guard let data = try handle.read(upToCount: size - start) else { return nil }
            let lines = data.split(separator: 10, omittingEmptySubsequences: false)
            // 尾块可能从上一条消息中间开始；不能将截断内容当作独立 JSON。
            let completeLines = lines.dropFirst(start == 0 ? 0 : 1)
            var latest: LifecycleEvent?
            for line in completeLines.reversed() {
                if let event = decodeEvent(line) { latest = event; break }
            }
            if latest == nil, let prior, prior.path == file.path, prior.size < size, start < prior.size {
                // 未漏过追加区间才能沿用已知状态；存在空档时宁可返回未知。
                latest = prior.event
            }
            if cache.count >= 160 && prior == nil { cache.removeAll(keepingCapacity: true) }
            cache[id] = Entry(path: file.path, size: size, modified: modified, event: latest)
            return latest
        } catch { return nil }
    }

    private static let markers = ["task_started", "task_complete", "turn_aborted"].map { Data($0.utf8) }
    private func decodeEvent(_ line: Data.SubSequence) -> LifecycleEvent? {
        guard Self.markers.contains(where: { line.range(of: $0) != nil }),
              let object = (try? JSONSerialization.jsonObject(with: line)) as? JSONObject,
              object["type"] as? String == "event_msg", let payload = object["payload"] as? JSONObject,
              let kind = payload["type"] as? String, let stamp = object["timestamp"] as? String,
              let date = fractional.date(from: stamp) ?? iso.date(from: stamp) else { return nil }
        let state: ThreadState
        switch kind {
        case "task_started": state = .running
        case "task_complete": state = (payload["error"] == nil || payload["error"] is NSNull) ? .completed : .attention
        case "turn_aborted": state = .stopped
        default: return nil
        }
        return LifecycleEvent(state: state, date: date)
    }
}
