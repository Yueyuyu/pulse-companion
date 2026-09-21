# Pulse Companion 架构与维护边界

## 当前入口与职责

`install.ps1` 默认部署 Pulse WebView2 后台版。表现层来自同级 `ui-design-lab/systems/pulse-desktop`；Windows 宿主与真实业务在本仓库。旧 WinForms 胶囊/任务灯保留为回退路径，不是当前默认界面。

```text
Codex Desktop 存在检测 ──> 后台宿主显示 / 隐藏
                              │ 运行时建立只读连接
独立 codex.exe app-server <────┘
        │
        ├─ account/rateLimits/read ──> 周额度与重置时间
        └─ thread/list + 只读 JSONL ─> 任务 / 关注 / 状态变化通知
                                       │
                              PulseLiveModel / PulseLiveRuntime
                                       │ schema v2 按应用快照
                              WebView2 + Lab Pulse renderer
                                       │ 用户主动点击任务
                              UUID 校验 → codex://threads/<UUID>
```

- `PulseLiveRuntime` 协调连接、后台显隐、托盘、发布快照和命令；任务每 2 秒刷新，额度每 60 秒主动刷新。
- `PulseLiveModel` 复用 `QuotaParser`、`TaskParser`、`WatchedTaskCollection` 与原持久化；不把估计进度当真实完成百分比。
- 外层 schema v2 包含 `source: companion-live`、`sequence`、`applications`；每项包含 `id`、`iconMode` 和原 schema v1 状态快照。
- 宿主和 renderer 当前只接受 `codex` 真实适配器；应用命令校验 applicationId。Cursor 仅是 Lab 示例，不读取其账号。
- `--live --background` 使用 `Local\CodexQuotaOverlay.SingleInstance`，与旧实时版互斥，避免重复通知和设置竞争。示例/隔离验证不占用该实时锁。

伴侣与 Codex 分属独立进程和目录，不挂钩或注入 Codex，不写其安装、账户、配置与会话，不创建任务或发送模型请求。后台每 2 秒检测 Codex 存在；退出时停止只读客户端并隐藏窗口，托盘仍驻留。手动退出托盘后不会自行复活，需下次登录或运行 `start.ps1`。

## 本机路径与兼容标识

| 路径 / 标识 | 职责 |
| --- | --- |
| `prototypes/pulse-desktop/webview/` | 当前 WebView2 宿主源码，目录名为历史遗留 |
| `bin/pulse-webview-preview/` | 本机 Pulse 构建输出，Git 忽略，不可公开打包 |
| `%LOCALAPPDATA%\CodexDesktopCompanion\pulse\<部署 ID>` | 已部署 Pulse 运行副本，EXE 仍叫 `PulseWebPreview.exe` |
| `%LOCALAPPDATA%\CodexDesktopCompanion\pulse-install.json` | 活动 Pulse 部署记录；目录 ID 不是产品版本 |
| `%LOCALAPPDATA%\CodexDesktopCompanion\app` | 保留的 `CodexQuotaOverlay.exe` / `CodexQuotaProbe.exe` 旧版 |
| `%LOCALAPPDATA%\CodexDesktopCompanion\install.json` | 旧版安装元数据 |
| `%LOCALAPPDATA%\CodexDesktopCompanion\startup-before-pulse.lnk` | 首次迁移时备份的启动项，回退前校验目标 |
| Startup 中的 `Codex Desktop Companion.lnk` | 沿用文件名，当前指向活动 Pulse 部署，参数为 `--live --background` |
| `%LOCALAPPDATA%\CodexQuotaOverlay\task-light.json` | 关注任务与置顶；保留旧版位置兼容 |
| 同目录 `pulse-window.json` | Pulse 位置、固定、贴边和缩放 |
| 同目录 `pulse-applications.json` | 按应用图标偏好；原子写入，损坏不覆盖，失败回滚 |
| 同目录 `pulse-runtime.json` | PID、连接和显隐等运维心跳，不含任务标题或凭据 |
| `%LOCALAPPDATA%\CodexCompanionPulsePreview\WebView2` | 独立 WebView profile，不复用 Codex 的浏览器 profile |

对外产品名称为 **Pulse Companion**。EXE、namespace、虚拟资源域名、Mutex、安装/设置目录和启动快捷方式文件名保留兼容标识，不做全局替换。改这些标识需要单独迁移方案和回退验证；GitHub 仓库及本地源码目录已改为 `pulse-companion`，不影响独立部署副本。

构建部署与源码分离，移动仓库不会影响已运行副本。`repair.ps1` 默认重新部署 Pulse；非同级 Lab 用 `install-pulse.ps1 -UiLabPath ...`。失败恢复先前记录和启动项，不修改 Codex 更新器。

## 重点关注、通知与跳转

- 最多 5 个关注任务，按选择顺序保存；状态刷新不自动重排，完成不自动移除。
- Codex 离线、任务已归档或未包含在最近 80 条结果中时保留记录并显示暂不可用。
- 只保存 UUID、短标题、最后状态和时间，不保存消息正文、凭据或完整 App Server 响应。设置仍有个人信息，不提交公开仓库。
- `TaskLightNotifier` 比较快照：首次连接只建基线，新进入需处理或执行中变完成才提醒；断线重置基线。
- `TaskToastForm` 在鼠标所在屏幕工作区右下角显示自绘、透明、置顶且不抢焦点的通知；8 秒后收起，悬停暂停计时。失败时回退托盘气泡，不宣称写入 Windows 通知中心。
- 任务行和通知共用 `CodexThreadNavigator`；只接受有效 UUID，通过注册的 `codex://threads/<UUID>` 协议跳转，不创建/写入任务。

## 窗口与动效

Pulse 原图标、64px 黑色侧轨、配色和动作由 Lab 共享 renderer 提供。Windows 仅负责透明合成、可见区域命中、拖动、贴边和缩放。

紧凑/展开共用固定透明画布，侧轨不在网页内横移后由原生窗口反向补偿；Region 仅接收可见侧轨/面板，透明预留区不挡桌面点击。`UseLayoutRounding=false` 保留抗锯齿，不能用贴边二值 Region 剪掉半透明软边缘。位置、拖动、吸附和显示器选择按可见侧轨定位。

未固定时移出约 320ms 自动收起，重入取消；点击不隐式固定。透明 WebView2 的离开检测由本窗口范围的只读指针检测补足，不安装全局输入钩子。固定展开与始终置顶独立。

## 验证与分发边界

- Lab 和宿主不带 `--live` 的模式只用示例；不能用它们证明真实账户接入。
- `--verify` 检查 24 个示例窗口状态、抗锯齿可见像素与 OS Region、固定画布；不是全部物理 DPI/壁纸验收。
- `--live-self-test` 使用隔离设置；`--verify-live` 只读真实 RPC→DOM，禁止通知、跳转和真实设置修改。
- `verify-pulse.ps1 -StrictLive` 检查已安装副本、唯一实例、Startup、心跳和额度/任务就绪，不替代视觉检查。
- 当前 CI 只构建共享业务和旧 WinForms 回退版，不包含需本机素材的 Pulse 构建。
- 机器人许可未解决，带 `LOCAL-ONLY.json` 的 renderer/部署输出不能公开分发。上游及品牌许可见 Lab `systems/pulse-desktop/UPSTREAM.md`。

## 更新影响

| 变化 | 对 Codex 的影响 | 对 Pulse Companion 的影响 |
| --- | --- | --- |
| Companion 安装/升级/改展示名 | 不改 Codex、不改变额度 | 更新独立运行副本，保留设置 |
| Codex 常规更新 | 不被 Companion 阻止 | 通常无影响，仍应验证只读接口兼容 |
| Codex 左下角布局变化 | 无 | 默认 Pulse 不依赖该布局；旧胶囊可能需适配 |
| App Server 协议变化 | 无 | 状态离线并重试，需要兼容修复 |
| 移动/删除源码目录 | 无 | 已部署副本继续运行；更新需重新取得源码与依赖 |

旧 WinForms 的 Quiet Workspace 视觉记录见 [旧版说明](legacy-quiet-workspace.md) 和 [design-qa.md](../design-qa.md)，不作为 Pulse 当前呈现的验收证明。
