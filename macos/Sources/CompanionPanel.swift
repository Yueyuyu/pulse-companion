import AppKit
import SwiftUI

@MainActor
enum CompanionPanel {
    static let size = NSSize(width: 360, height: 462)

    static let icon: NSImage? = {
        guard let location = Bundle.main.url(forResource: "Pulse", withExtension: "icns") else { return nil }
        return NSImage(contentsOf: location)
    }()

    static func statusIcon() -> NSImage? {
        let badge = icon?.copy() as? NSImage
        badge?.size = NSSize(width: 23, height: 23)
        return badge
    }

    static func make(_ model: Dashboard) -> NSPopover {
        let popover = NSPopover()
        popover.behavior = .transient
        popover.contentViewController = NSHostingController(rootView: CompanionPanelView(model: model))
        popover.contentSize = size
        return popover
    }

    static func offscreenBitmap(_ model: Dashboard) -> NSBitmapImageRep? {
        let renderer = ImageRenderer(content: CompanionPanelView(model: model))
        renderer.proposedSize = ProposedViewSize(width: size.width, height: size.height)
        guard let image = renderer.cgImage else { return nil }
        return NSBitmapImageRep(cgImage: image)
    }
}

@MainActor
private struct CompanionPanelView: View {
    @ObservedObject var model: Dashboard
    @State private var watchOnly = false
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                if let mark = CompanionPanel.icon {
                    Image(nsImage: mark).resizable().frame(width: 38, height: 38).accessibilityHidden(true)
                }
                VStack(alignment: .leading, spacing: 2) {
                    Text("Pulse Companion").font(.headline)
                    HStack(spacing: 5) {
                        Circle().fill(model.connected ? Color.green : Color.secondary).frame(width: 6, height: 6)
                        Text(verbatim: model.status).font(.caption).foregroundStyle(.secondary)
                    }
                }
                Spacer()
                Button { model.tick(force: true) } label: { Image(systemName: "arrow.clockwise") }
                    .buttonStyle(.borderless).disabled(model.paused).help("立即刷新").accessibilityLabel("立即刷新")
                Menu {
                    Button(model.paused ? "恢复 Codex 读取" : "暂停 Codex 读取", action: model.togglePause)
                    Button(model.alerts ? "关闭任务通知" : "启用系统任务通知…", action: model.toggleAlerts)
                    Button(model.loginStatus == .enabled ? "关闭登录时启动" :
                           (model.loginStatus == .requiresApproval ? "打开系统登录项设置…" : "登录时启动"), action: model.toggleLogin)
                    Divider()
                    Text("当前仅接入 Codex，其他应用暂不支持")
                    Button("退出 Pulse Companion") { NSApp.terminate(nil) }
                } label: { Image(systemName: "ellipsis.circle") }
                .menuStyle(.borderlessButton).fixedSize().help("设置与退出").accessibilityLabel("设置与退出")
            }

            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline) {
                    Text("本周剩余额度").font(.subheadline.weight(.semibold))
                    Spacer()
                    if let quota = model.quota {
                        Text(verbatim: String(quota.remaining) + "%").font(.title2.weight(.semibold)).monospacedDigit()
                    } else { Text("不可用").font(.callout).foregroundStyle(.secondary) }
                }
                if let quota = model.quota {
                    ProgressView(value: Double(quota.remaining), total: 100).tint(.orange)
                    if let reset = quota.reset {
                        Text("重置：" + reset.formatted(date: .abbreviated, time: .shortened)).font(.caption).foregroundStyle(.secondary)
                    } else { Text("未提供重置时间").font(.caption).foregroundStyle(.secondary) }
                } else {
                    Text(model.connected ? "未取得有效的周额度；请确认 Codex 已登录。" : "连接已登录的 Codex 后显示额度与重置时间。")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            .padding(12).frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 10, style: .continuous))

            Picker("任务视图", selection: $watchOnly) {
                Text("最近任务").tag(false)
                Text(verbatim: "已关注 \(model.favorites.ids.count)/\(Favorites.limit)").tag(true)
            }.pickerStyle(.segmented).labelsHidden()

            ScrollView {
                LazyVStack(alignment: .leading, spacing: 3) {
                    if watchOnly {
                        if model.favorites.ids.isEmpty { placeholder("还没有关注的任务", "在最近任务右侧点星标，最多保留 \(Favorites.limit) 项。") }
                        ForEach(model.favorites.ids, id: \.self) { id in taskRow(id, model.feed[id]) }
                    } else if let rows = model.feed.rows {
                        if rows.isEmpty { placeholder("没有最近任务", "Codex 尚未返回未归档的对话。") }
                        ForEach(rows) { task in taskRow(task.id, task) }
                    } else {
                        placeholder("任务列表暂不可用", model.connected ? "等待读取结果，或使用右上角刷新。" : model.status)
                    }
                }.padding(.vertical, 2)
            }.frame(maxHeight: .infinity)
            Divider()
            Text(verbatim: model.note ?? "仅只读查看 · 点击任务返回 Codex")
                .font(.caption).foregroundStyle(.secondary).lineLimit(2).fixedSize(horizontal: false, vertical: true)
        }
        .padding(16).frame(width: CompanionPanel.size.width, height: CompanionPanel.size.height, alignment: .topLeading)
    }

    private func placeholder(_ title: String, _ detail: String) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(verbatim: title).font(.callout.weight(.medium))
            Text(verbatim: detail).font(.caption).foregroundStyle(.secondary)
        }.frame(maxWidth: .infinity, alignment: .leading).padding(.vertical, 10)
    }

    private func taskRow(_ id: UUID, _ task: CompanionThread?) -> some View {
        let title = task?.title ?? CompanionThread.fallbackTitle(id)
        let saved = model.favorites.ids.contains(id)
        return HStack(spacing: 8) {
            Button { model.open(id) } label: {
                VStack(alignment: .leading, spacing: 3) {
                    Text(verbatim: title).lineLimit(1).truncationMode(.tail).font(.callout)
                    Text(verbatim: task?.state.rawValue ?? "暂不可用").font(.caption)
                        .foregroundStyle(task?.state == .attention ? Color.orange : Color.secondary)
                }.frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
            }.buttonStyle(.plain).help("在 Codex 中打开对话")
            Button { model.toggleFavorite(id) } label: {
                Image(systemName: saved ? "star.fill" : "star").foregroundStyle(saved ? Color.orange : Color.secondary)
                    .frame(width: 26, height: 24)
            }.buttonStyle(.plain).help(saved ? "取消关注" : "关注任务").accessibilityLabel(saved ? "取消关注" : "关注任务")
        }.padding(.vertical, 3)
    }
}
