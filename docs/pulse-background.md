# Pulse Companion 后台运行

这是外置后台程序，不是注入 Codex 的内部插件；使用体验是不必单独开 App。Windows 当前用户计划任务负责登录启动，每 2 秒检查登记的 AI 桌面应用：逐项显示已打开的应用，全部关闭则隐藏，后台托盘仍在。关闭 Codex 停止其客户端；其他应用关闭后暂停新额度查询、保留授权。后台意外退出后按每分钟的周期恢复；通常在一分钟左右重新拉起，Windows 调度或休眠可能延后。

正常运行时忽略重复触发，不会周期性移动、缩放或重开窗口。托盘“退出并暂停自动恢复”和 `stop.ps1` 是用户主动停止，暂停跨登录保留；需要 `start.ps1` 或重新安装才恢复。暂停通过 `%LOCALAPPDATA%\CodexQuotaOverlay\pulse-background.pause` 表达，不删除关注或位置设置。

产品原名 Codex Desktop Companion。当前对外名称、GitHub 仓库与源码目录已更名；`PulseWebPreview.exe`、`Codex Desktop Companion.lnk`、安装/设置路径与 Mutex 均保留兼容；`1.2.0` 仍为未发布开发版本。仓库改名不会让已安装副本自动更新产品展示名，需明确执行安装/修复部署；运行路径及个人设置保持不变。

## 应用与图标

八项应用分别检测，浮条只显示已打开的应用，每页最多三项。最小化到任务栏仍显示，关闭窗口且仅剩后台/托盘进程时隐藏；全部关闭就隐藏浮条。关闭应用暂停新的额度查询，不删除登录授权或偏好，重开自动恢复。Codex 保留完整任务功能；Cursor / Claude / Grok Bot 按已获授权优先读取本机有效来源，再只读复用各自桌面登录，独立网页登录仅作手动备用。本机来源解析已验证，真实服务端额度仍需打开对应应用验收。其余四项额度、所有新应用任务能力明确未接入。Lab 仍使用固定示例。详见 [能力矩阵与授权流程](account-authorization.md)。

展开应用面板后点「品牌图标 / 机器人」，或使用托盘的「Codex 图标」菜单。按应用 ID 保存至 `%LOCALAPPDATA%\CodexQuotaOverlay\pulse-applications.json`。沿用现有 Codex 机器人默认值；写盘失败回滚，损坏文件不覆盖。品牌用 Pulse 原 SVG；机器人沿用本机隔离资源，授权未解决，不公开分发。

原有关注文件 `task-light.json` 不迁移、不清空；位置/固定存于 `pulse-window.json`，运行心跳存于 `pulse-runtime.json`（仅 PID、显隐、连接状态等，不含凭据或会话正文）。任务、通知、UUID 跳转仍使用现有业务模块。

## 安装、更新与恢复

需 Windows、WebView2、Node，以及同级 `ui-design-lab` 源码与已合法准备的本机 Pulse 资源。可给 install-pulse.ps1 显式传入 `-UiLabPath`；缺失素材会停止构建，不下载未授权素材。部署后不依赖 Lab/Vite 或仓库路径。新电脑不能把机器人的私有构建上传到 GitHub 再拉取，见 Lab 的 UPSTREAM.md。

当前桌面构建无“仅品牌图标、免机器人资源”的路径；选用品牌图标也需要满足上述资源校验。两个仓库要有配套 Pulse 代码，Lab 构建前需 `npm ci`。完整步骤见 [新电脑恢复指南](new-computer-setup.md)。

```powershell
.\install.ps1                       # 默认本机 Pulse，部署前自测
.\verify.ps1 -StrictLive             # 需要 Codex 已打开登录
.\stop.ps1                          # 停止并暂停自动恢复
.\start.ps1                         # 清除暂停，由 Windows 启动正常后台
.\repair.ps1                        # 新部署目录，保留旧副本
.\rollback-pulse.ps1                # 恢复原启动项和旧组件
.\uninstall.ps1                     # 删除安装目录和启动项，保留设置
```

计划任务名为 `\Pulse Companion`，仅当前用户的已登录交互会话运行，不保存密码、不提权。设置登录触发和一分钟恢复周期，运行时长不设 72 小时上限，电池状态不阻止启动。`start.ps1` 请求 Task Scheduler 启动，不再从 Codex 的子进程链直接托管常驻 EXE。

首次迁移备份 `startup-before-pulse.lnk`，计划任务注册校验成功后移除旧 Startup 入口；部署记录是 `pulse-install.json`。安装失败恢复旧任务定义、记录、快捷方式和暂停状态；卸载/旧版回退先移除本项目任务。程序使用原单实例 Mutex，避免重复通知。未装过旧版的电脑没有回退目标，脚本会明确停止。不会关闭真正的 Codex，也不修改其可执行文件、更新器或额度。

## 验证边界

桌面开合使用固定透明合成画布，侧轨不再在 WebView 内横移后由原生窗口反向补偿；透明预留区不属于 Windows 命中区域。紧凑/展开/贴边检查同时核对稳定画布、可见像素未被裁切和透明区域穿透；320ms 离开宽限及固定行为不变。

前端用示例检查分项、图标切换与收起；宿主用真实只读数据验证 RPC→DOM。后台显隐可以在真实 WPF 窗口注入隔离的进程存在结果验证，不为了测试强制关闭用户 Codex。应用内 100/125/150/200% 缩放不等于实际多显示器物理 DPI 全覆盖；自动登录启动仍需用户下一次正常登录观察，不主动注销电脑。

日常后台检查用 `verify.ps1 -StrictLive`；启动/暂停逻辑使用 `scripts/test-pulse-background.ps1` 与 `--live-self-test`，都不移动窗口。`-Verify` 会主动切换 24 个状态，`-VerifyLive` 会展开和显隐测试窗口，必须事先告知用户；它们不是后台“复检”，也不会由计划任务自动执行。无参数双击 EXE 现在只进入正常后台，示例要求显式 `--demo`。
