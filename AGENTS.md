# Codex Desktop Companion 项目规则

本项目用于维护外置的 Codex Desktop 桌面增强。处理安装、修复或功能改动前，先读 `README.md`、`docs/architecture.md` 和相关源码。

## 不可破坏的边界

- 禁止修改、替换、注入或反编译后重打包 Codex 安装目录。
- 数据访问保持只读；不得创建任务、发送模型请求或写入 Codex 会话日志。
- 不记录或输出 Token、Cookie、账号密码及完整账户响应。
- 除非已有迁移方案，不要改动 `CodexQuotaOverlay.exe`、单实例 Mutex、namespace 或 `%LOCALAPPDATA%\CodexQuotaOverlay` 设置目录，它们承担旧版本兼容。
- Codex 协议或布局变化时，先复现和验证，再做最小适配；不要通过关闭校验或伪造状态掩盖失败。

## 新电脑安装流程

用户要求安装或恢复时：

1. 运行 `git status --short`，保留用户已有改动；
2. 运行 `.\install.ps1`；
3. 运行 `.\verify.ps1 -StrictLive`；若 Codex 未登录或主窗口未打开，说明实时项未验证，不把它描述为程序故障；
4. 检查只有一个 `CodexQuotaOverlay.exe`，且路径位于 `%LOCALAPPDATA%\CodexDesktopCompanion\app`；
5. 检查 `shell:startup` 中的 `Codex Desktop Companion.lnk` 指向同一位置。

安装脚本应保持幂等：重复运行等同于升级/修复，不丢失任务灯位置与置顶设置。

## 修改后的最低验证

```powershell
.\build.ps1
.\bin\CodexQuotaProbe.exe --self-test
.\bin\CodexQuotaProbe.exe --task-self-test
.\bin\CodexQuotaProbe.exe --codex-path-probe
.\bin\CodexQuotaProbe.exe --probe
.\bin\CodexQuotaProbe.exe --task-probe
```

涉及窗口、阴影、透明边缘、位置或动效时，还要启动程序并进行实际视觉验证；静态检查不能替代视觉验收。Codex 左下角定位依赖当前版本布局，验证时确保 Codex 主窗口存在。

未经用户明确要求，不执行 `git commit`、`git push`、发布 Release 或删除旧源码目录。
