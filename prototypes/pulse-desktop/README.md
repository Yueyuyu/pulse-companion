# Pulse Companion Windows 宿主

此目录包含当前实际运行的 WebView2 宿主、示例/验证入口，以及保留的早期 WPF 草稿。`prototypes` 目录和 `PulseWebPreview.exe` 文件名为历史兼容标识，**不表示当前只有静态原型**。

## 当前真实入口

日常安装与启动在仓库根目录操作：

```powershell
.\install.ps1
.\verify.ps1 -StrictLive
.\start.ps1
```

默认安装构建 Lab 的共享 renderer，并部署到 `%LOCALAPPDATA%\CodexDesktopCompanion\pulse\<部署 ID>`，当前用户 `\Pulse Companion` 计划任务使用 `--live --background`。Windows 登录驻留、意外退出后每分钟恢复、随 Codex 显隐，无独立任务栏入口；保留额度、任务、关注、通知和跳转。真实接入仅 Codex，当前版本 `1.2.0` 尚未发布。

完整安装与回退见 [后台说明](../../docs/pulse-background.md) 和 [新电脑恢复指南](../../docs/new-computer-setup.md)。运行不依赖 Vite；改源码后不会自动更新已部署副本。

## 开发与验证入口

以下命令在仓库根目录运行。非同级 Lab 通过 `-UiLabPath` 指定，不将开发者机器路径视为约定。

```powershell
# 构建本机宿主与共享 renderer，不安装、不切换自启
.\prototypes\pulse-desktop\build-webview.ps1 -UiLabPath ..\ui-design-lab

# 固定样例窗口与独立控制台，不接真实账户
.\prototypes\pulse-desktop\build-webview.ps1 -Run -UiLabPath ..\ui-design-lab

# 24 组窗口检查 + 隔离业务自测 + 真实只读 RPC→DOM
.\prototypes\pulse-desktop\build-webview.ps1 -Verify -VerifyLive -UiLabPath ..\ui-design-lab

# 仅示例窗口检查时提供任务栏入口；正常模式不显示
.\prototypes\pulse-desktop\build-webview.ps1 -Run -InspectWindow -UiLabPath ..\ui-design-lab
```

| EXE 参数 | 行为与边界 |
| --- | --- |
| 无参数 | 正常真实后台，等同 `--live --background`；暂停时不创建窗口 |
| `--demo` | 示例窗口与控制台，不读取账号或跳转真实任务 |
| `--live --background` | 已安装后台版；真实数据、通知、跳转、持久化，与旧版共享实时 Mutex |
| `--live` | 前台开发实时模式，不具有按应用存在自动隐藏的后台策略；不要与已安装实时实例重复运行 |
| `--verify <报告目录>` | 24 个固定样例状态；可与实时版共存，不写用户设置 |
| `--live-self-test <报告文件>` | 隔离业务/应用偏好测试，不读真实账号、不通知或跳转 |
| `--verify-live <报告文件>` | 只读真实数据进入 DOM，并验证后台显隐；临时设置，不通知、不跳转、不修改真实关注 |
| `--inspect-window` | 仅检查时使窗口出现在任务栏，方便选取；不是日常启动参数 |

根目录 `start-pulse.ps1 -InspectWindow` 会暂停后台自动恢复、切换已识别伴侣进程来检查真实窗口，结束后运行 `stop.ps1` 再 `start.ps1` 恢复正常后台。日常不要将它当作无副作用的示例命令。根目录 `start.ps1 -Development` 仍指向旧 WinForms 实现，不是 Pulse。

`-Verify` / `-VerifyLive` 会显示测试窗口、切换位置或状态，必须提前告知用户；普通后台修复不运行它们。可用 `scripts/test-pulse-background.ps1` 检查 Windows 任务定义，用 `--live-self-test <报告文件>` 检查隔离启动/业务逻辑，再只读 `verify.ps1 -StrictLive` 检查安装副本。

## 技术和依赖

- `webview/`：透明 WPF + Microsoft WebView2 CompositionControl，负责窗口/命中区域/后台协调；业务复用根 `src/`。
- 界面来自 Lab 的 `systems/pulse-desktop/desktop`，与 Gallery 复用 React/SVG 组件及 Pulse 原版样式/动作；Lab 始终是示例数据。
- 使用 Microsoft.Web.WebView2 `1.0.4191.47` SDK（构建时下载 NuGet），运行需 Evergreen WebView2 Runtime；没有引入 Electron。
- WebView profile 独立于 Codex；禁止外部导航、弹窗、下载和权限申请，bridge 不提供任意 shell/文件能力。
- 动态机器人仅限合法准备的本机隔离资源；缺失时构建停止。输出 `bin/pulse-webview-preview` 含 `renderer/LOCAL-ONLY.json`，不可公开上传；许可证和来源见 Lab `UPSTREAM.md`。

## 验证范围

`--verify` 覆盖左右两侧、100/125/150/200% 应用渲染缩放、紧凑/展开/贴边，共 24 状态。要求稳定透明画布、展开不移动侧轨所在原生窗口、可见像素不被 OS Region 误裁、透明预留区排除。关闭布局取整以保留抗锯齿。

报告在 `artifacts/pulse-webview/`，必须对应本次源码、本次执行时间与正常退出码。内容/像素/Region 检查不是全桌面截图，也不是多物理显示器 DPI 或所有壁纸的完整验收；Windows 登录自动恢复仍需下一次正常登录观察。

早期边缘根因与历史记录见 [EDGE-VALIDATION.md](EDGE-VALIDATION.md)。不要把历史报告当本次通过结论。

本目录的 `build.ps1` 默认转发 WebView2 构建，只有 `-Legacy` 才启用早期 WPF 自绘草稿；同目录旧 `.cs` / `tokens.json` 保留历史，不作为当前运行/设计基准。仓库根目录的 `build.ps1` 则仅构建共享业务/旧 WinForms 版，注意区分。
