# Pulse Companion 新电脑恢复指南

当前默认入口是 Pulse 后台浮条，旧版左下角额度胶囊与白色任务灯仅用于回退。产品名为 Pulse Companion，GitHub 仓库与本地源码目录已统一为 `pulse-companion`；安装目录与设置路径仍保留兼容名称。

## 前置条件

- Windows 10/11 x64、Git、Windows PowerShell 5.1、.NET Framework 4.x；
- 已安装 Microsoft Edge WebView2 Evergreen Runtime；
- Node.js 和 npm，版本满足配套 `ui-design-lab` 依赖要求，建议使用受支持的 LTS；
- Codex Desktop 已安装；读取真实额度、任务时需已打开并登录；
- 两个仓库均需包含配套的 Pulse 代码；当前 `1.2.0` 尚未发布，Lab 的 Pulse 改动尚未随本次 Companion 推送，需单独取得，不能假定默认克隆已包含；
- 合法取得并单独准备的本机机器人资源。不能从 GitHub Release 复制未获分发许可的资源，也不会由安装脚本自动下载。

构建时会下载 Microsoft WebView2 SDK `1.0.4191.47`（NuGet），输出在被忽略的 `bin/`。不需要 Python 或管理员权限。**“不需要 Node.js”只适用于旧 WinForms 版，不能用于当前默认安装。**

## 准备工作树

在存放项目的父目录执行：

```powershell
git clone https://github.com/Yueyuyu/pulse-companion.git
git clone https://github.com/Yueyuyu/ui-design-lab.git
Set-Location .\ui-design-lab
npm ci
Set-Location ..\pulse-companion
```

先确认 Companion 存在 `install-pulse.ps1`，Lab 存在 `scripts/build-pulse-desktop.mjs`。若缺失，应取得配套代码，而不是把公开旧 Release 当新版使用。

再按 Lab 的 `systems/pulse-desktop/UPSTREAM.md` 准备本机资源。当前桌面构建会校验 `.local-cache/pulse-bot/`，即使计划只显示品牌图标也不能跳过。**仅克隆公开源码不足以保证当前桌面构建成功**；缺失时应停止并说明条件，不上传或公开打包机器人资源。

## 首次安装

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
.\verify.ps1 -StrictLive
```

非同级 Lab 路径使用：

```powershell
.\install-pulse.ps1 -UiLabPath 'D:\Projects\ui-design-lab'
```

`-ExecutionPolicy Bypass` 仅影响这次 PowerShell 进程，不改变长期系统策略。

安装会构建、复制到 `%LOCALAPPDATA%\CodexDesktopCompanion\pulse\<部署 ID>`、运行隔离自测、备份原启动项并设置 `--live --background`。部署之后不需要 Vite、Node.js 或源码目录保持在线。

确认：

- 打开 Codex 后显示 Pulse 浮条，而不是旧左下角胶囊；
- 无独立任务栏按钮，托盘可展开、刷新、退出；
- 实时额度与任务就绪，点击任务跳到对应对话；
- 未固定时移出自动收起，拖动后位置可恢复；
- 正常关闭 Codex 后浮条隐藏，下次打开会显示；
- 下一次正常登录 Windows 后自动驻留。不要为了验证强制注销或关闭用户正在执行的任务。

## 迁移个人设置（可选）

复制前在相关电脑停止伴侣，复制后启动：

```powershell
.\stop.ps1
# 将需要保留的设置放到下表的同名路径
.\start.ps1
```

| `%LOCALAPPDATA%\CodexQuotaOverlay\` 下的文件 | 内容 |
| --- | --- |
| `task-light.json` | 最多 5 个关注任务的 UUID、短标题、最后状态与置顶偏好；兼容旧任务灯位置 |
| `pulse-window.json` | Pulse 位置、固定、贴边与缩放；新显示器布局不同可不迁移此文件 |
| `pulse-applications.json` | 每个应用的品牌图标/机器人偏好 |

这些文件含个人任务信息，不应提交公开 Git。它们不含账户凭据、消息正文或完整会话。新电脑无法访问的关注任务会保留为暂不可用，可手动取消；迁移设置不会迁移 Codex 登录或对话。

不要复制 `pulse-install.json`、`pulse-runtime.json`、Startup 快捷方式或 WebView profile 来代替安装：它们包含机器相关路径、部署信息或运行时状态。

## 后续更新与回退

取得两个仓库配套的新代码；Lab 依赖变化时重新 `npm ci`，然后在 Companion 根目录执行 `.\repair.ps1`。非同级 Lab 路径仍用 `install-pulse.ps1 -UiLabPath ...` 重新部署。

普通开机、Codex 常规更新或切换网络不需要再次安装。若要恢复旧界面，使用 `.\rollback-pulse.ps1`；它要求原旧版程序及启动备份均存在，首次纯 Pulse 安装不能凭空回退。见 [旧版说明](legacy-quiet-workspace.md)。

## 交给 Codex 处理

在此仓库打开 Codex，可说明：

> 请按本项目 AGENTS.md 在这台电脑安装 Pulse Companion。先核实配套 Lab 和合法本机资源；满足条件后安装并严格验证。不要修改 Codex 安装文件，不要提交、推送或公开分发机器人资源。
