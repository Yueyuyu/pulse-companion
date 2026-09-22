# Pulse Companion 故障排查

默认检查 Pulse 后台版。旧左下角胶囊/白色任务灯的问题见文末，不要按旧版 WinForms 的渲染规则修改 WebView2 宿主。

## 构建或安装失败

1. 确认 Companion 与 `ui-design-lab` 都有当前配套的 Pulse 源码，而不是只有公开旧 Release。
2. 确认 Windows x64、.NET Framework、Node/npm、WebView2 Runtime 可用；在 Lab 执行 `npm ci`。
3. 非同级 Lab 用 `.\install-pulse.ps1 -UiLabPath 'D:\Projects\ui-design-lab'`。
4. “本机 Pulse 资源缺失”是素材前置条件不满足，不是账户故障。按 Lab `UPSTREAM.md` 合法准备本机资源；当前品牌模式也不能跳过该校验。不要上传机器人缓存或关闭校验。
5. SDK 下载失败时检查到 NuGet 的连接，不关闭 TLS 校验或系统安全功能。

依赖齐全后可运行 `.\repair.ps1` 重建并部署，保留个人设置。该命令会更新本机安装和启动项，不是只读诊断。

## 托盘在，但桌面没有浮条

这是后台版的正常显隐策略之一：Codex 未运行时隐藏；打开 Codex 后约 2 秒检测到并显示。没有单独任务栏按钮也是预期行为。先运行：

```powershell
.\verify.ps1
.\verify.ps1 -StrictLive    # Codex 已打开且已登录时
```

校验会检查安装副本、唯一进程、当前用户计划任务及恢复策略、近期心跳和实时状态；失败信息应保留，不伪装正常。托盘主动退出或 `stop.ps1` 会暂停自动恢复，跨登录保留；运行 `.\start.ps1` 才重开。

如果已安装但未运行，使用 `.\start.ps1`。若路径冲突或存在多个伴侣实例，确认后用 `.\stop.ps1` 再启动；不要结束 Codex 本体，不要手工复制多个 EXE 到 Startup。

若提示“缺少匹配的后台计划任务”，旧安装还没有恢复机制，运行 `.\repair.ps1` 迁移。当前任务名为 `\Pulse Companion`，只有一个当前用户任务，正常运行时忽略每分钟重复触发；意外退出后通常一分钟左右重新拉起。Windows 计划任务服务停止、系统休眠或任务被外部禁用时不能保证恢复时限，先检查任务与系统状态，不绕过权限校验。

Codex 的 Windows 桌面包内部进程可能叫 `ChatGPT.exe`；应结合 `OpenAI.Codex_...\app\ChatGPT.exe` 安装路径识别，不能仅凭进程名认为它是另一款应用。

## 额度或任务未就绪

- 确认 Codex 已安装、打开并登录；额度约 60 秒主动刷新，任务约 2 秒刷新，也可从托盘立即刷新。
- `.\verify.ps1 -StrictLive` 失败表示本次真实数据未就绪，不能用 Lab 的示例页面代替。
- 如需进一步诊断，先 `.\build.ps1`，再用 `.\bin\CodexQuotaProbe.exe --codex-path-probe`、`--probe`、`--task-probe` 做只读检查。
- Codex 刚升级时可在任务安全结束后正常重启应用再检查；协议变化需最小适配，不能改配置或把固定百分比当真实值。

`-Offline` 在 Pulse 安装下只跳过“实时数据必须就绪”的要求，不会让正在运行的后台程序停止联网。

## 关注暂不可用 / 点击任务没有跳转

关注最多 5 个。离线、App Server 失败、归档或不在最近 80 条返回结果中时，会保留记录并显示暂不可用。恢复只读数据后自动更新；不再关注时手动取消星标，不要删除整个 `task-light.json`。

任务跳转要求有效 UUID 和 Windows 已注册的 `codex://threads/<UUID>` 协议。先确认对应对话仍可在 Codex 中访问；查看界面错误提示，必要时修复 Codex 自身协议注册。不要通过搜索任务标题猜测并打开另一条任务。

## 没收到完成通知

首次连接只建立基线，不补发历史完成记录；断线后重新建立基线。只有后续新进入需处理或从执行中变为完成才通知。

当前使用右下角自绘桌面通知，8 秒后收起、悬停暂停、点击跳转；自绘失败才回退托盘气泡。它不是 Windows 通知中心的原生 Toast，因此不能以通知中心没有历史记录认定没有提醒。

## 悬停不收起、闪动、锯齿

- 先检查是否固定展开；未固定时离开约 320ms 收起，重入取消。置顶与固定是两个不同设置。
- 确认当前运行的是已重新部署的源码版本；只修改 Lab、Vite 页面或构建 `bin/` 不会自动替换安装副本。
- Pulse 使用 WebView2/WPF 透明合成，必须保留 `UseLayoutRounding=false`；Region 只排除透明空白，不剪掉软边缘。
- 开合必须保持固定透明画布与侧轨坐标，不能恢复“网页横移 + 原生窗口反向缩放”。
- `prototypes/pulse-desktop/build-webview.ps1 -Verify` 会主动移动、缩放并切换 24 个测试状态，执行前必须告知用户；它不是正常后台启动或定时自检。仅修复后台启动时不需要运行。多屏物理 DPI 和不同壁纸仍需人工检查。详见 [宿主说明](../prototypes/pulse-desktop/README.md)。

## 改名后仍看到旧名字

源码产品名为 Pulse Companion；已安装 EXE 不会随仓库编辑自动更新，明确更新时用 `.\repair.ps1`。即使更新后，文件名 `PulseWebPreview.exe`、`CodexDesktopCompanion` 安装目录和 `CodexQuotaOverlay` 设置目录也有意保留，避免丢失状态。旧 `Codex Desktop Companion.lnk` 仅保留在迁移备份/回退流程，当前后台入口已改为 `\Pulse Companion` 计划任务。

GitHub 仓库和本地源码目录已改为 `pulse-companion`；已有克隆可用 `git remote set-url origin https://github.com/Yueyuyu/pulse-companion.git` 更新远程地址。不要手动移动设置或批量替换 namespace/Mutex。完整对照见 [README](../README.md)。

## SmartScreen / 安全软件提示

当前构建未签名，先核对来源、构建日志与文件哈希，不关闭系统防护。可检查本机输出：

```powershell
Get-FileHash .\bin\pulse-webview-preview\PulseWebPreview.exe -Algorithm SHA256
```

## 旧 Quiet Workspace 回退模式

恢复方式见 [旧版说明](legacy-quiet-workspace.md)。仅当正在运行旧版时：

- 左下角胶囊挡住 Codex 按钮：检查 `src/CodexWindowTracker.cs` 与真实窗口定位；默认 Pulse 不依赖该区域。
- 白色任务灯阴影/边缘：检查 `LayeredWindowRenderer`，不恢复 `CS_DROPSHADOW` 或硬裁切；不能把该规则套到 Pulse 的可见区域穿透实现上。
- 旧版重建使用 `.\install.ps1 -Legacy`，且不能有活动 Pulse 安装记录；`.\repair.ps1` 默认会升级到 Pulse。
