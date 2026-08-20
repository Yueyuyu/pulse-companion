# 新电脑恢复指南

## 前置条件

- Windows 10 或 Windows 11（64 位）
- 已安装 Codex Desktop；要读取实时额度和任务，需要在 Codex 中完成登录
- Git 与 Windows PowerShell 5.1
- Windows 的 .NET Framework 4.x 功能可用

项目不需要 Node.js、Python、管理员权限或额外 NuGet 包。

## 首次恢复

```powershell
git clone https://github.com/Yueyuyu/codex-desktop-companion.git
cd .\codex-desktop-companion
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

安装结束后确认：

- 白色任务灯出现在桌面，可拖动、点击展开；
- 打开 Codex 时，左下角能看到周额度百分比；
- `.\verify.ps1 -StrictLive` 没有失败；
- 注销或重启 Windows 后，组件会自动回来。

`-ExecutionPolicy Bypass` 只作用于这一次 PowerShell 进程，不会修改系统的长期执行策略。

## 迁移个人位置设置（可选）

任务灯的位置和置顶开关不放进 Git，因为它们属于每台电脑的本机状态。如果想沿用旧电脑的位置，可复制：

```text
%LOCALAPPDATA%\CodexQuotaOverlay\task-light.json
```

复制前先运行 `.\stop.ps1`，复制后再运行 `.\start.ps1`。文件不含账号凭据。

## 后续更新

```powershell
git pull
.\repair.ps1
```

普通重启、Codex 日常升级或切换网络均不需要再次安装。

## 交给 Codex 自动处理

在仓库根目录打开 Codex，然后说明：

> 请按本项目 AGENTS.md 在这台电脑安装 Codex Desktop Companion，并完成严格实时验证；不要修改 Codex 安装文件，也不要提交或推送。

Codex 会使用仓库内的固定脚本，不需要依赖上一台电脑的聊天记录。
