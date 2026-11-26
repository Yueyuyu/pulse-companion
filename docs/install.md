# 安装 Pulse Companion

Windows 安装程序与 Mac 磁盘镜像来自同一份 Companion 源码，各自使用原生桌面宿主。本文适用于 [1.2.0-rc.1 品牌图标公开预览](https://github.com/Yueyuyu/pulse-companion/releases/tag/v1.2.0-rc.1)。在该页 **Assets** 中获取安装包和同一版本的校验清单；旧版源码 ZIP 不能代替安装程序。

## 先选文件

| 文件 | 适用设备 | 含义 |
| --- | --- | --- |
| `Pulse-Companion-1.2.0-Windows-x64-Setup.exe` | 64 位 Intel / AMD Windows 10 或 11 | 双击安装，无需开发工具。不是 Windows ARM64 原生包。 |
| `Pulse-Companion-1.2.0-macOS-universal.dmg` | macOS 13 或更新；Apple Silicon 或 Intel | 含本项目的通用原生 .app 和「应用程序」入口。 |
| `SHA256SUMS.txt` | 两个平台 | 本次构建的 SHA-256，对应同一版本的两个文件。 |

`Source code (zip)` / `Source code (tar.gz)` 由 GitHub 为每个标签自动生成，**不是额外的安装方式**。

## 校验与签名

把安装包和同一 Release 下的 `SHA256SUMS.txt` 下载到一个目录。Windows 在该目录打开 PowerShell：

```powershell
Get-FileHash .\Pulse-Companion-1.2.0-Windows-x64-Setup.exe -Algorithm SHA256
```

Mac 在终端打开下载目录：

```sh
shasum -a 256 Pulse-Companion-1.2.0-macOS-universal.dmg
```

将结果与清单中**同名文件**的 64 位哈希逐字比对，忽略字母大小写。哈希不匹配不要运行。哈希一致只能说明文件与发布附件一致，不能代替开发者签名、Apple 公证或安全软件检查。

本次预览的 Windows 安装程序没有 Authenticode 发行签名；Mac 应用使用 ad-hoc 临时签名以校验包内容，没有 Apple Developer ID 签名，也未公证。其他版本以各自的发布说明为准。如公司策略、SmartScreen 或 Gatekeeper 不允许启动，请停止安装或联系管理员；不要为此关闭防护、删除隔离属性或改系统安全策略。

如果你是第一次遇到这些词：**签名**让系统核对“由哪一位发行者签发、文件此后是否改变”；**Apple 公证**是苹果对提交的软件进行自动检查后发放凭据，不等于上架 App Store。即便已经签名，Windows 仍可能对信誉不足的新文件提示。见[微软的 SmartScreen 说明](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)和[苹果的公证说明](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)。

## Windows：安装和使用

1. 需要 Windows 10/11 x64、系统 .NET Framework 4.x 与 [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。如果缺少 WebView2，请从微软安装。
2. 运行 `…-Windows-x64-Setup.exe`，按当前用户安装。打开已登录的 Codex，查看系统托盘和桌面浮条；没有支持的应用窗口时，浮条隐藏。安装不需要管理员密码、Git、Node.js 或 Lab 仓库。
3. 若已运行本机专用机器人版，公开安装包不会替换它。要继续使用机器人版，请留在已有安装上，按照[原来的源码更新步骤](new-computer-setup.md)操作。不要删除设置或运行两个后台实例来强行切换版本。
4. 升级公开版时运行新版公开 EXE。卸载从 Windows「设置 → 应用 → 已安装的应用」选择 Pulse Companion；个人偏好默认保留。

若安装向导提示「未能完成激活」（静默安装退出码 `30`），表示程序文件已安装，但当前用户的后台任务没有配置成功。检查任务计划和安装目录权限后，请用**同一份安装程序**重试；也可从「已安装的应用」卸载。不要手动清除关注、设置或备份目录。重试会核对原包的登记信息；升级失败不承诺自动恢复上一个版本的二进制。

公开版默认关闭非 Codex 应用的额度读取。Cursor、Claude、Grok Bot 使用各自的官方网页授权流程；Antigravity 使用已经安装并登录的官方 agy CLI。你可以只启用需要的应用，停止后跨重启保留。公开版不提取其他桌面应用的会话，也不继承本机专用版的授权。不同产品的来源见[按应用说明](account-authorization.md)。不要上传账户状态、关注任务名称或 WebView 用户目录作为排障附件。

## macOS：安装和使用

1. 打开 `…-macOS-universal.dmg`，将 **Pulse Companion.app** 拖入「应用程序」。复制完成后弹出磁盘镜像，再从「应用程序」启动，不在只读镜像中长期运行。
2. 打开并登录官方 Codex 桌面端（部分版本的应用文件名为 ChatGPT.app），点击菜单栏里的 Pulse Companion 查看周额度和最近的任务。它按应用身份识别正在运行的桌面端，使用随应用提供的命令行组件，不要求单独安装 CLI。此版只读取 Codex；Windows 支持的其他产品额度不会出现在 Mac 中。
3. 需要提醒时在 Companion 中启用通知，并在「系统设置 → 通知」允许提示。任务行可打开原 Codex 对话。Mac 端不要求输入 Codex 密码。
4. 更新前先从菜单栏退出 Companion，再用新版 DMG 替换同名 .app。卸载时退出应用，将「应用程序」内的 Pulse Companion 移到废纸篓。两端设置独立，不建议跨系统直接复制个人配置。

## 仍然看不到额度？

先确认打开的是已登录的 **Codex 桌面应用**，不是只有终端中的 CLI；再查该平台的[支持范围](../README.md#支持哪些应用)。显示「不可用」与显示「剩余 0%」不是一回事。Windows 可进一步查阅[后台说明](pulse-background.md)与[故障排查](troubleshooting.md)。
