# Pulse Companion 项目规则

本项目原名 Codex Desktop Companion，当前对外名称为 Pulse Companion。处理安装、修复或功能改动前，先读 `README.md`、`docs/architecture.md` 与相关源码。已有未提交改动必须保留，不能把工作树中的新功能误称为已发布。

## 当前默认入口（2026-09-21）

- `install.ps1` 默认部署 Pulse WebView2 后台版；共享 renderer 在同级 `ui-design-lab/systems/pulse-desktop`，Windows 宿主在 `prototypes/pulse-desktop/webview/`。历史目录/EXE 名称不是“尚未接真实数据”的证据。
- 用户已授权此前将 Pulse 部署到独立本机目录，备份并切换原 Startup 入口为 `--live --background`；不与旧版同时自启，旧程序和回退入口保留。此为当前状态，覆盖早期原型阶段“不改启动项/仅样例”的限制，不代表每次文档修改都应重装。
- 当前代码已接入真实额度、任务、关注持久化、通知和跳转；目前只有 Codex 真实适配器。Cursor 只在 Lab 示例中，不能宣称已接入。
- 每个已接入应用独立条目和品牌/机器人选择；图标偏好按应用 ID 保存。Lab 继续固定示例，与实时数据隔离。
- 沿用 Pulse 图标、造型、配色与动作，不重画另一套。Quiet Workspace 与早期 WPF 自绘草稿保留为历史/回退，不覆盖 Lab 的原设计套系。
- 当前开发版本 `1.2.0` 未发布；机器人授权未解决，仅限本机隔离缓存和构建，不公开上传、打包或作为 Release。先读 Lab `systems/pulse-desktop/UPSTREAM.md`。

## 不可破坏的边界

- 禁止修改、替换、注入或反编译后重打包 Codex 安装目录。
- 数据访问保持只读；不得创建任务、发送模型请求或写入 Codex 会话日志。
- 不记录或输出 Token、Cookie、账号密码及完整账户响应；个人关注标题也不应提交公开 Git。
- 对外名称统一 Pulse Companion，但保留 `PulseWebPreview.exe`、`CodexQuotaOverlay.exe`、namespace、Mutex、虚拟资源域名、设置/安装路径及 `Codex Desktop Companion.lnk` 兼容标识。没有明确迁移方案不得全局替换。
- 用户已要求将 GitHub 仓库与本地目录统一为 `pulse-companion`，并将当前 Companion 改动提交、推送到 `main`。不因此提交配套 Lab 的其他改动、不发布 Release；以后仍须遵循明确授权。
- 真实 Pulse 与旧版共用单实例锁，避免重复通知/设置竞争；不迁移或清空原关注记录。
- Codex 协议或布局变化时先复现、验证，再最小适配；不关闭校验或伪造正常状态。

## 窗口与交互回归边界

- 紧凑/展开使用稳定透明合成画布，侧轨坐标不变；不可恢复网页内横移加原生窗口反向移动/缩放补偿。
- Region 只接收可见侧轨/面板，透明预留区穿透桌面点击；拖动、贴边、显示器选择按侧轨定位。
- `UseLayoutRounding=false` 保留 WebView 合成抗锯齿，不硬裁半透明边缘。
- 未固定时临时展开须自动收回；点击不隐式固定。保留 320ms 离开宽限、重入取消、键盘与贴边规则。
- 透明 WebView2 用本窗口范围只读指针检测补偿 leave；不装全局鼠标钩子，不吞其他应用输入。

## 安装与恢复

用户明确要求安装、更新运行副本或恢复时：

1. 运行 `git status --short`，保留已有改动；核实配套 Lab、npm 依赖、WebView2 与合法本机素材。
2. 默认 `.\install.ps1`；非同级 Lab 使用 `.\install-pulse.ps1 -UiLabPath ...`。
3. `.\verify.ps1 -StrictLive`；Codex 未打开/登录时说明真实项未验证，不伪造成功。
4. 检查唯一 `PulseWebPreview.exe` 位于 `%LOCALAPPDATA%\CodexDesktopCompanion\pulse\<部署 ID>`，后台运行且无任务栏入口；旧版回退模式才检查 `app\CodexQuotaOverlay.exe`。
5. 检查 Startup 的 `Codex Desktop Companion.lnk` 指向同一安装副本，参数为 `--live --background`；不启动开发目录示例副本充当正式运行。

重复安装应保留关注、位置、固定和图标偏好。`.\rollback-pulse.ps1` 需已有旧版与启动备份；首次纯 Pulse 安装不可虚构回退目标。

## 修改后的验证

共享业务/旧版的最低检查：

```powershell
.\build.ps1
.\verify.ps1 -Development -Offline
.\bin\CodexQuotaProbe.exe --codex-path-probe
.\bin\CodexQuotaProbe.exe --probe
.\bin\CodexQuotaProbe.exe --task-probe
```

涉及 Pulse 宿主时还要构建 `prototypes/pulse-desktop/build-webview.ps1`；根据改动执行 `-Verify`（示例视觉）和 `-VerifyLive`（隔离自测及真实只读 RPC→DOM），清楚区分验证层次。版本信息变更时核对 `VERSION`、`src/Properties/AssemblyInfo.cs`、`src/app.manifest` 与活动 `prototypes/pulse-desktop/preview.manifest`。

涉及窗口、阴影、透明边缘、位置或动效时必须做实际视觉验证，不能只用编译结果替代。24 组应用缩放不等于所有物理 DPI/壁纸通过；不为验收强制注销 Windows 或中断用户任务。文档/展示名变更不隐含重新安装或抢占鼠标操作。

未经用户明确要求，不执行 `git commit`、`git push`、GitHub 仓库重命名、发布 Release 或删除旧源码目录。
