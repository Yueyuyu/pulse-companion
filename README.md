# Pulse Companion

[![Release](https://img.shields.io/github/v/release/Yueyuyu/pulse-companion?display_name=tag&sort=semver)](https://github.com/Yueyuyu/pulse-companion/releases/latest)

Pulse Companion（原 Codex Desktop Companion）是外置、只读的 Windows 桌面伴侣：用 Pulse 风格浮条集中显示 AI 应用的额度与任务状态，离开应用窗口后也能关注任务、接收完成提醒，并一键跳回对应对话。

当前开发版本为 **`1.2.0`（尚未发布）**。历史版本 [`v1.1.0`](https://github.com/Yueyuyu/pulse-companion/releases/tag/v1.1.0) 仍是 Quiet Workspace 旧版，不包含当前 Pulse 后台界面。本仓库保存当前开发源码，不能把旧 Release 当新版安装包。配套 `ui-design-lab` 的 Pulse 改动尚需单独提交和推送，本次不包含该仓库。

> 对外产品名已改为 **Pulse Companion**；GitHub 仓库和本地目录已统一为 `pulse-companion`。这是独立社区项目，不是上游 [Pulse](https://github.com/qunqin24/Pulse) 或 OpenAI 的官方 Windows 版本。不会修改或注入 Codex 安装文件。

## 当前功能

| 能力 | 当前实现 |
| --- | --- |
| 后台自动运行 | 安装一次后随当前用户登录 Windows 启动；Codex 运行时显示，退出后隐藏并停止只读连接。没有独立任务栏按钮，托盘仍可刷新、展开或退出。 |
| 真实剩余额度 | 从 Codex App Server 读取周额度，用额度环与百分比展示，详情显示重置时间；读取失败时显示不可用，不用示例数字冒充真实数据。 |
| 实时任务状态 | 汇总需要处理、执行中和最近完成的任务；任务约每 2 秒刷新，额度约每 60 秒主动刷新，也可立即刷新。不是任务完成百分比或推测进度条。 |
| 重点关注 | 最多关注 5 个任务，按选择顺序持久化；完成后仍保留，离线或暂时未返回时显示暂不可用，不自动删除。 |
| 完成与待处理提醒 | 任务进入需处理或从执行中变为完成时，在屏幕右下角弹出不抢焦点的通知，点击可打开任务。当前是自绘桌面通知，失败时回退到系统托盘气泡，不是 Windows 通知中心的原生 Toast。 |
| 点击任务跳转 | 校验任务 UUID 后通过 `codex://threads/<UUID>` 请求 Codex 打开对应对话，不创建或修改任务。 |
| 紧凑、展开、贴边 | 可拖动、贴边收起；未固定时移出约 320ms 自动收起，重新移入取消收起。固定展开与始终置顶是独立设置，位置和偏好保存在本机。 |
| 按应用分项 | 每个已接入应用有独立状态和图标偏好，可选品牌图标或机器人。**目前只有 Codex 真实接入**；Cursor 仅为 Lab 多应用示例，安装 Cursor 不会自动接入。 |

界面沿用 Pulse 的图标、黑色侧轨、造型、配色与动作，只做 Windows 适配；业务保留 Companion 的真实数据、关注、通知和跳转。紧凑与展开使用固定透明画布，避免开合时侧轨跳动；透明预留区域不拦截桌面点击。

旧版“Codex 左下角额度胶囊 + 白色任务灯”不再是默认界面，也不与 Pulse 实时版同时运行。历史截图、功能和恢复方式见 [Quiet Workspace 旧版说明](docs/legacy-quiet-workspace.md)。

## 安装与更新

当前是**本机源码构建**，不是可直接公开分发的安装包。需要：

- Windows 10/11 x64、Windows PowerShell 5.1、Git、.NET Framework 4.x；
- 已安装 Microsoft Edge WebView2 Evergreen Runtime；
- Node.js（建议受支持的 LTS，满足 Lab 依赖要求）及 npm；
- `ui-design-lab` 的配套 Pulse 源码与 npm 依赖；
- 合法准备的本机机器人资源。**当前桌面构建即使选品牌图标，也会检查机器人资源；仅克隆两个公开仓库尚不能保证完成当前构建。**

建议保留同级目录：

```text
Project/
├─ pulse-companion/   Pulse Companion 宿主与业务
└─ ui-design-lab/             Pulse Desktop 共享界面与设计测试
```

在包含当前 Pulse 代码和上述依赖的工作树中执行：

```powershell
.\install.ps1
.\verify.ps1 -StrictLive    # 需要 Codex 已打开并登录
```

安装会构建共享界面与 Windows 宿主，部署到独立目录，运行隔离自测，备份并切换当前用户 Startup 快捷方式，最后以 `--live --background` 启动。旧程序、关注记录和设置保留。部署完成后**不依赖源码路径、Node.js、Lab 或 Vite 服务持续运行**。

后续拉取配套新代码后运行 `.\repair.ps1`；日常打开 Codex 不需要再次安装。非同级 Lab 路径可使用 `.\install-pulse.ps1 -UiLabPath 'D:\Projects\ui-design-lab'`。完整准备、换机和设置迁移见 [新电脑恢复指南](docs/new-computer-setup.md)。

| 命令 | 用途 |
| --- | --- |
| `.\install.ps1` / `.\repair.ps1` | 安装或重新构建并部署默认 Pulse 后台版 |
| `.\start.ps1` / `.\stop.ps1` | 启动或停止已安装版本 |
| `.\verify.ps1 -StrictLive` | 校验启动项、唯一进程、后台心跳与真实额度/任务就绪 |
| `.\verify.ps1 -Offline` | Pulse 安装下检查启动项、进程和心跳，不要求实时数据就绪；不等于阻止运行中的程序联网 |
| `.\rollback-pulse.ps1` | 已有旧程序和启动备份时，恢复 Quiet Workspace 旧版 |
| `.\uninstall.ps1` | 卸载运行副本与启动项，默认保留设置 |

## 名称与兼容性

产品、GitHub 仓库与源码目录已统一命名；安装/设置等历史标识保留，不迁移个人设置：

| 项目 | 当前值与原因 |
| --- | --- |
| 对外产品名 | **Pulse Companion**；README、窗口/托盘文案、程序产品元数据和安装说明统一使用 |
| GitHub / 源码目录 | `Yueyuyu/pulse-companion` / `pulse-companion`；旧仓库链接由 GitHub 重定向 |
| 当前宿主文件名 | `PulseWebPreview.exe`，虽然名称保留 Preview，`--live --background` 已是真实后台入口 |
| 旧版文件名 | `CodexQuotaOverlay.exe` / `CodexQuotaProbe.exe`，保留回退与验证兼容 |
| 安装目录 | `%LOCALAPPDATA%\CodexDesktopCompanion\pulse\<部署 ID>`；旧版在 `app\` |
| 开机启动项 | `Codex Desktop Companion.lnk`，沿用原文件名以避免重复启动项 |
| 设置与互斥锁 | `%LOCALAPPDATA%\CodexQuotaOverlay`、`Local\CodexQuotaOverlay.SingleInstance`，避免丢失关注与重复通知 |

`task-light.json` 保存关注与置顶，`pulse-window.json` 保存窗口位置/固定/贴边/缩放，`pulse-applications.json` 保存按应用图标偏好。它们是本机个人状态，不应提交 Git。仅显式运行 `.\uninstall.ps1 -RemoveSettings` 才会一并删除设置。

## 与 Codex、额度和更新的关系

- 只调用 `account/rateLimits/read`、`thread/list` 并只读本机任务日志，不创建任务、不发送模型请求，不消耗模型额度；
- 不读取或记录凭据，不修改 Codex 安装、账户、会话或更新器；
- Codex 更新不会覆盖独立运行副本，本项目也不会阻止 Codex 更新；
- App Server 协议变化仍可能需要兼容修复，失败时显示离线并重试，不伪造正常状态；
- 默认 Pulse 浮条不依赖 Codex 左下角布局；旧额度胶囊只保留为回退功能。

详见 [架构与维护边界](docs/architecture.md)、[后台运行说明](docs/pulse-background.md) 和 [故障排查](docs/troubleshooting.md)。

## 开发与验证

| 入口 | 数据与用途 |
| --- | --- |
| Lab 的 `#/systems/pulse-desktop/playground` | 固定示例，用于设计与多应用测试；不连接真实账户 |
| `prototypes/pulse-desktop/build-webview.ps1 -Run` | Windows 宿主示例窗口与控制台；不代替实时版 |
| `prototypes/pulse-desktop/build-webview.ps1 -Verify` | 左右侧 × 4 档应用缩放 × 3 个状态，共 24 组画布/边缘检查 |
| `prototypes/pulse-desktop/build-webview.ps1 -VerifyLive` | 隔离业务自测 + 真实只读 RPC→DOM 检查，不通知、不跳转、不保存真实关注 |
| `build.ps1` + `verify.ps1 -Development -Offline` | 共享业务与旧 WinForms 回退实现的构建/离线自测，**不构建 Pulse renderer** |

Pulse 宿主源码仍在 `prototypes/pulse-desktop/webview/`，属于历史目录命名，不代表真实功能尚未接入。示例/实时模式、可执行参数与验证边界见 [宿主开发说明](prototypes/pulse-desktop/README.md)。应用内缩放检查不能替代多物理显示器 DPI、壁纸和正常登录后的人工验收。

当前 GitHub Actions 仅构建共享业务与旧 WinForms 回退程序，不产出 Pulse 桌面安装包。它的成功不等于 Pulse Windows 视觉或实时验收通过。当前可执行文件均未签名，首次运行可能触发 SmartScreen 或安全软件扫描。

## 设计来源与分发限制

UI 由 `ui-design-lab/systems/pulse-desktop` 提供，与 Quiet Workspace 独立。上游 [Pulse](https://github.com/qunqin24/Pulse) 的固定来源 commit 为 `2e17225ece661138de9ce9c73b322c7c1b16d753`；品牌 SVG、移植代码和各自许可见 Lab 的 `systems/pulse-desktop/UPSTREAM.md` 与 `licenses/`。

机器人素材的第三方授权仍未解决，不能将上游根许可证当作全部素材的授权。机器人仅在被 Git 忽略的本机缓存中；`bin/pulse-webview-preview` 和部署输出含 `LOCAL-ONLY.json`，**不得上传 GitHub、公开网站或 Release**。当前名称更新不改变这一分发边界，也没有增加新应用适配器。

## 版本规则

[`VERSION`](VERSION) 是产品版本来源；程序集版本与活动应用清单保持一致。`1.2.0` 的变化仍放在 [`CHANGELOG.md` 的 Unreleased](CHANGELOG.md)，不把本机部署 ID 当作发布版本。正式版本使用 `vX.Y.Z` Git Tag 与 Release，只有明确要求发布并满足分发条件后才创建。

产品采用 [Semantic Versioning](https://semver.org/lang/zh-CN/)：不兼容变更增加主版本、向后兼容功能增加次版本、修复增加补丁版本。本次同步名称和说明不另起一个已发布版本。
