# 红框应用接入核实

状态：已有独立登录基础，2026-09-22 按新授权增加 Cursor / Claude / Grok Bot 桌面自动读取，**不是七项功能全部完成**。当前只有 Codex 完整实时任务适配；新应用逐项来源、额度验收边界见 [账户授权说明](account-authorization.md)，不得把安装探测或历史记录说成任务实时接通。

## 请求范围和本机证据

| 应用 | 已核实 | 尚需解决 |
| --- | --- | --- |
| Cursor | Windows 安装存在；本机 `User/globalStorage/state.vscdb` 有 `composerHeaders` 和 `cursorDiskKV` | 任务状态字段语义、完成事件、跳转与额度授权；不能只凭对话存在推断正在执行 |
| Claude | Windows Store 桌面包存在；`plan-usage-history.json` 为 v2 历史样本结构 | 本次历史样本的 `u` 均为空，不能生成剩余百分比；桌面对话、Cowork 和 Claude Code 必须区分 |
| Grok Bot | 独立 Windows 应用，产品元数据为 Grok Bot / SpaceXAI；与 GrokDesk 不同 | 上游额度使用 Cursor 账户体系，不是 Grok CLI / SuperGrok；任务状态入口仍需核实 |
| ZCode | `.zcode/v2/tasks-index.sqlite` 有任务 ID、标题、状态、更新时间、归档/删除标记；存在明确 completed/error 状态 | 实时运行/待处理状态映射、完成事件和准确任务跳转；Coding Plan 的国际/国内账户不能混用 |
| Kimi | 安装的是 `kimi-desktop` 桌面应用 | 不能用 Kimi Code API key 的额度冒充桌面 Kimi / Kimi Work 额度；本机尚未确认可用额度和任务接口 |
| 豆包工作 | 独立 DoubaoWork Windows 应用及专用 User Data 存在 | 尚未确认可用额度和任务状态接口；进程运行不等于任务执行 |
| WorkBuddy | `.workbuddy/workbuddy.db` 的 sessions 表包含真实会话 ID、标题、status、更新时间；有 session_usage 表 | 运行/待处理状态更新语义、完成通知与任务深链；session_usage 不是订阅剩余额度 |

本次仅只读核实安装信息、数据结构和非敏感状态，不修改第三方安装、登录配置或任务记录；不输出任务标题、账户响应、Cookie 或 Token。未启动模型请求，也未将这些应用部署为“已完整接入”。

## 上游可复用部分与限制

固定来源：[Pulse commit 2e17225](https://github.com/qunqin24/Pulse/tree/2e17225ece661138de9ce9c73b322c7c1b16d753)。

- [Cursor](https://github.com/qunqin24/Pulse/blob/2e17225ece661138de9ce9c73b322c7c1b16d753/Docs/providers/cursor.md)：上游借用编辑器登录信息查询未公开保证稳定的 usage-summary 路由；区分 Cursor Models 与 Other Models 的额度池，不能全部标成周额度。
- [Grok Bot](https://github.com/qunqin24/Pulse/blob/2e17225ece661138de9ce9c73b322c7c1b16d753/Docs/providers/grok-bot.md)：独立 allowance；需校验订阅确实包含额度，缺失 usagePercent 不是 0%。未返回重置时间时不得按开始时间加七天猜测。上游提供官方登录页加轮询的登录流程，不称作 OAuth，不读取 sand-secrets.json。
- [Claude](https://github.com/qunqin24/Pulse/blob/2e17225ece661138de9ce9c73b322c7c1b16d753/Docs/providers/claude-code.md)：CLI OAuth、桌面会话、status-line 和缓存是不同来源。桌面安装存在，不表示 CLI 凭据或 status-line 已配置。
- [Kimi Code](https://github.com/qunqin24/Pulse/blob/2e17225ece661138de9ce9c73b322c7c1b16d753/Docs/providers/kimi-code.md)：需要 Coding API key，不能推导桌面 Kimi 产品的订阅能力。
- [Z.ai / 智谱](https://github.com/qunqin24/Pulse/blob/2e17225ece661138de9ce9c73b322c7c1b16d753/Docs/providers/zai.md)：按账户来源选择主机和额度，禁止将某区域凭据发送给另一主机。
- 上游的 `TencentBuddyReader`、`ZCodeReader` 主要聚合 token 使用历史，不是现成的 Windows 任务实时状态、完成通知和跳转适配器。使用量历史与剩余额度必须分开。

## 已确认的授权边界

2026-09-22 用户进一步明确允许必要时只读复用对应桌面软件的登录令牌，仅访问该产品官方额度接口；不读取密码、不导出凭据、不刷新第三方令牌、不改原应用配置。优先本机接口/有效缓存，已有桌面登录其次，独立网页登录只作手动备用。官方主机不代表额度端点承诺稳定公开 API，必须分别说明。

如果某应用没有可用的授权或只读状态入口，需要明确报告具体缺口，不能偷偷缩减成“七个图标”、用静态数据顶替，或把不支持显示为读取故障。

正式接线时分别验证：应用身份与存在、额度来源/周期、真实任务状态、按应用隔离的关注、通知去重、任务跳转；保留 Codex、后台恢复与原个人设置。Lab 使用独立示例，不读取真实账户。完成并验证前不替换本机稳定运行副本。
