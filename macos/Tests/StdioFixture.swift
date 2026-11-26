import Foundation

// 仅由 test.sh 编译到临时目录，不随 .app 分发。用生产进程传输层真实收发逐行 JSON。
@main
enum StdioFixture {
    static func send(_ object: [String: Any]) {
        var data = try! JSONSerialization.data(withJSONObject: object)
        data.append(10)
        let midpoint = data.count / 2
        FileHandle.standardOutput.write(data.prefix(midpoint))
        FileHandle.standardOutput.write(data.suffix(from: midpoint))
    }

    static func main() {
        guard Array(CommandLine.arguments.dropFirst()) == ["app-server"] else { exit(10) }
        var handshaken = false, ready = false, reads = 0
        while let line = readLine() {
            guard let obj = (try? JSONSerialization.jsonObject(with: Data(line.utf8))) as? [String: Any],
                  let method = obj["method"] as? String else { exit(11) }
            let id = obj["id"] as? Int ?? -1
            switch method {
            case "initialize":
                guard !handshaken, let params = obj["params"] as? [String: Any],
                      let meta = params["clientInfo"] as? [String: String], meta["name"] == "pulse_companion" else { exit(12) }
                handshaken = true
                send(["id": id, "result": ["userAgent": "offline-fixture"]])
            case "initialized":
                guard handshaken, !ready, id == -1 else { exit(13) }
                ready = true
            case "account/rateLimits/read":
                guard ready else { exit(14) }
                let weekly: [String: Any] = ["windowDurationMins": 10080, "usedPercent": 37,
                                              "resetsAt": Date().addingTimeInterval(6000).timeIntervalSince1970]
                send(["id": id, "result": ["rateLimits": ["secondary": weekly]]])
            case "thread/list":
                guard ready, let params = obj["params"] as? [String: Any],
                      params["archived"] as? Bool == false, params["useStateDbOnly"] as? Bool == true,
                      params["limit"] as? Int == 80, params["sortKey"] as? String == "updated_at",
                      params["sortDirection"] as? String == "desc" else { exit(15) }
                reads += 1
                // server request 与待处理客户端请求故意同 ID，不能消费或批准它。
                send(["id": id, "method": "item/commandExecution/requestApproval", "params": [:]])
                let flags = reads == 1 ? [] : ["waitingOnUserInput"]
                let row: [String: Any] = ["id": "10000000-0000-4000-8000-000000000001", "name": "隔离测试",
                                          "status": ["type": "active", "activeFlags": flags] as [String: Any]]
                send(["id": id, "result": ["data": [row]]])
            default: exit(16)
            }
        }
    }
}
