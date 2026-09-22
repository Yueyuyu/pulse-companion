# 自动桌面读取与备用网页登录（开发中）

用户于 2026-09-22 明确授权：优先桌面本机额度接口/有效缓存，再必要时只读复用该应用现有登录令牌；独立网页登录只作为备用。此决定更新 2026-09-21 的独立登录限制。仅向对应产品固定官方额度接口发请求；不读取密码、不导出或持久化借用凭据、不读取/刷新第三方 refresh token、不改原应用登录、不发送推理请求。独立网页中的密码、验证码仍由用户自行输入。

## 当前能力矩阵

| 应用 | 自动来源 / 备用方式 | 任务、关注、完成通知、跳转 |
| --- | --- | --- |
| Codex | 现有 App Server 只读周额度，无需本次网页登录 | 现有完整功能保留 |
| Cursor | 指定 `cursorAuth/accessToken` 字段，只读原 SQLite（含 WAL）；查询自己的 usage-summary。备用为官方页面 + PKCE。个人/团队 Auto 与 API 月池分别展示 | 未接入 |
| Claude | 当前组织两分钟内非空缓存优先；否则仅复用 Claude 桌面 sessionKey/sessionKeyV3，GET 当前组织 usage。备用 profile-only OAuth；5 小时、周和模型限制分别解析 | 桌面对话、Cowork、Claude Code 任务均未接入 |
| Grok Bot | 只读自身 sand-secrets 当前账号访问令牌，不借 Cursor 的另一个账号；查询独立 Grok Bot allowance。备用为 Cursor 官方账户体系登录 | 未接入 |
| ZCode | 未接入：未核实匹配桌面产品的网页登录额度协议 | 未接入：数据库状态语义与深链待验证 |
| Kimi | 未接入：不拿 Kimi Code API key 额度替代桌面产品 | 未接入 |
| 豆包工作 | 未接入：只读授权/额度协议待核实 | 未接入 |
| WorkBuddy | 未接入：任务上下文使用量不等于账户额度 | 未接入：数据库状态语义与深链待验证 |

2026-09-22 本机三个桌面来源均已完成只读解析验证（只输出成功标志，不输出凭据）；Claude 现有历史 `u` 全空，不能当有效缓存。验证时三个应用都没有打开桌面窗口，尚未查询它们的真实服务端额度。后续打开已登录应用即可验收，无需默认再次登录；可能受账户类型、订阅、地区或上游协议影响。七个新应用不是“全部完整接通”。进程存在只用于显隐，不推断任务执行/完成。

## 使用

1. 正常打开已登记的 AI 应用，浮条只显示已打开的应用。约 2 秒更新一次：关闭某应用就隐藏该项，全部关闭则隐藏整个浮条；最小化到任务栏仍算打开，仅残留后台/托盘进程不算。每页最多 3 项，下方箭头切换；原 Codex 关注和窗口设置不变。
2. Cursor、Claude、Grok Bot 自动尝试读取，每 60 秒刷新；详情标明 **桌面有效缓存 / 桌面登录 / 备用网页账户**。读不到时显示原因，不自动弹登录网页。
3. 只有需要备用方式时手动点 **备用网页登录**；固定官方入口，Claude 授权页可能显示 Claude Code 公共客户端名。5 分钟超时，可取消。授权成功后显式选择备用账户模式，可用 **改用桌面读取** 返回自动模式；不把备用账号说成原应用账号。
4. **停止读取** 取消查询、删除 Companion 备用授权、持久化 off 模式；不会注销原应用或重启后自动借令牌重连。**恢复自动读取** 才重新尝试。删除本机备用授权不等于服务端撤销；如需撤销，使用官方账户页面。关闭应用仅暂停新请求，重开继续原模式，不等于停止读取。

本机 DPAPI 凭据仅属于 Companion 的备用登录与当前 Windows 用户，不拷贝到 GitHub/新电脑；换机优先读取新电脑的应用登录，备用模式才需重新授权。损坏备用文件保留，停止读取后才重新授权。读取模式位于 accounts/<id>.mode，不含凭据。保留设置的卸载也保留这些文件；要清除请先停止读取，或明确移除所有设置。

## 实现与安全约束

- `PulseAccounts.cs`：应用白名单、DPAPI CurrentUser 凭据库、各产品额度解析；`PulseApplicationPresence.cs` 按本机应用桌面窗口判断每项是否显示，不读取窗口标题或内容。
- `PulseDesktopAccounts.cs` / `PulseDesktopStorage.cs`：仅打开对应应用固定路径，不扫描其他浏览器；SQLite 原位只读、短 busy 超时、有界字段；Windows 当前用户 DPAPI 解包应用 key，CNG AES-GCM 校验认证标签与 Cookie host digest。只接受已核实 v10，不绕过未知/app-bound 存储保护。只解密当前访问令牌和必要组织标识，不解密刷新令牌或其他账户。凭据不进入常驻账户对象、vault 或 WebView。
- Claude 只查询 `https://claude.ai/api/organizations/<当前 UUID>/usage`，不调用 bootstrap/账户资料接口来猜组织。缓存须匹配 lastActiveOrg 且采样不超过两分钟；空、过期、未来时间、不同组织均拒绝。组织不可确认时降级，不选择第一个组织。Grok Bot 显式退出时不回落旧账号。
- `PulseAccountLogin.cs`：Cursor/Grok Bot PKCE 轮询；Claude 随机 state + PKCE + loopback OAuth。先绑定随机端口，再打开浏览器；只接受 localhost 的匹配 GET 回调，校验路径/Host/state/重复参数、请求大小和超时。
- `PulseAccountHttp.cs`：固定 HTTPS 主机、路径、方法；禁用重定向和自动 Cookie；令牌只发送给匹配额度端点；响应最多 1 MiB，HTTP 限时，429 按 Retry-After 冷却。
- `PulseAccountService.cs`：已打开应用按单请求、60 秒查询；关闭后隐藏并暂停新查询，不删除授权。自动来源优先，缺失时才尝试原有备用授权并明确标记；桌面凭据存在但拒绝/失效时不偷偷切换其他账户。借用凭据每次重新发现，从不刷新。已发出的自有授权/令牌刷新允许完成；重开重新读取且不绕过 429 冷却。取消/停止/退出递增 generation，晚到结果不落盘或更新 UI。独立 OAuth 仅刷新 Companion 自己的授权。
- 凭据位于 `%LOCALAPPDATA%\CodexQuotaOverlay\accounts\<应用 ID>.dpapi`，原子替换；不保存 Cursor refresh token，不保存登录密码/验证码。
- WebView 只收到白名单业务状态和额度窗口，不接收凭据、授权码或完整账户响应。页面不能传入 URL。登录/取消/断开命令仅含应用 ID。
- 缺值不等于 0% 已用，也不等于 100% 剩余。已重置/陈旧读数隐藏；Claude 存在锁定或未知限制语义时不以其他窗口冒充健康状态。多个池不平均，侧轨使用首个有效窗口，详情列出全部有效窗口。
- 自动读取、停止、授权中、读取失败、暂无有效窗口、限流、未接入分别表达。Lab Gallery 仍使用示例，不读取真实账户，界面只消费经过白名单重建的来源枚举和读取模式。

官方登录页面不意味着额度端点有稳定的官方第三方 API 承诺。当前使用以下上游已实现的私有/未承诺稳定协议，失败必须诚实降级，不放宽证书校验、不绕过登录。

## 协议来源与许可

Windows 实现参考 [Pulse 固定 commit 2e17225](https://github.com/qunqin24/Pulse/tree/2e17225ece661138de9ce9c73b322c7c1b16d753)：

- `Sources/Pulse/Auth/CursorWebLogin.swift`、`CursorAppLogin.swift`、`OAuthLogin.swift`；
- `Sources/Pulse/Providers/CursorUsageService.swift`、`ClaudeCodeUsageService.swift`、`ClaudeDesktopSession.swift`；
- `Docs/providers/cursor.md`、`grok-bot.md`、`claude-code.md`。

上游为 Apache-2.0，许可副本见 [Pulse-Apache-2.0.txt](licenses/Pulse-Apache-2.0.txt)。本项目修改：Windows .NET Framework、只读系统 SQLite、DPAPI/CNG、固定额度端点、按当前桌面账户读取、手动备用网页登录、停止保护。Grok Bot 存储格式按本机 0.39.0 的访问路径/字段核实，未导入或重打包安装源码。品牌与机器人分发限制仍见 Lab `UPSTREAM.md`，本文件不授予第三方素材分发权。

## 验证边界

`--live-self-test` 覆盖 PKCE 标准向量、伪造/重复 state、固定端点/方法、DPAPI 往返和损坏保留、晚到刷新不得重建已断开的凭据、429、额度缺值/单位/过期/限制状态、验证模式不联网。前端隔离 WebView bridge 覆盖待登录→授权中→取消/超时→双池展示→断开、多应用分页、自动收起及正确应用 ID。

新增自动读取自测覆盖 AES-256-GCM 标准向量/损坏 tag、UTF-16 字段、仅额度路径、缓存时效/当前组织、Grok 当前账号退出、来源优先级、借用凭据不落盘/不进 DTO、停止/重启/恢复、发现期间关闭/取消、401 不跨账户回落。前端测试覆盖来源标记、自动读取中不弹备用入口、停止后恢复，以及桌面/窄屏交互。真实读取需以服务端返回为证；本机来源解析检查未联网，不等同额度验证。
