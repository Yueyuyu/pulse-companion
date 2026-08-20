# 架构与维护边界

## 数据流

```text
Codex Desktop 当前安装
        │ 发现 codex.exe
        ▼
独立 codex.exe app-server 子进程
        │ 只读 RPC
        ├─ account/rateLimits/read ──> 周额度胶囊
        └─ thread/list + 本机 JSONL ─> 桌面任务灯 / 任务详情 / 通知
```

伴侣程序与 Codex Desktop 分属不同进程和目录。它不会挂钩 Codex 进程，也不会向 Codex 安装包写文件。

## 运行与持久化

- 仓库：保存可审查的源码、原型、文档和脚本。
- 构建输出：仓库的 `bin/`，被 Git 忽略。
- 稳定运行副本：`%LOCALAPPDATA%\CodexDesktopCompanion\app`。
- 安装信息：`%LOCALAPPDATA%\CodexDesktopCompanion\install.json`，只记录来源路径和安装时间。
- 用户设置：`%LOCALAPPDATA%\CodexQuotaOverlay\task-light.json`，为兼容旧版保留原目录名。
- 自启动：当前用户 Startup 文件夹中的 `Codex Desktop Companion.lnk`。

部署副本与仓库分开，使仓库移动、Git 操作和开发构建不会打断正在运行的组件。`repair.ps1` 是从仓库重新部署到稳定运行目录的唯一正常更新入口。

## 进程模型

- `CodexQuotaOverlay.exe`：无控制台的 WinForms 主进程，使用本机 Mutex 保证单实例。
- `CodexQuotaProbe.exe`：同一源码的控制台验证入口，不常驻。
- `codex.exe app-server`：由主进程或实时探针启动的只读状态通道。

内部名称继续使用 `CodexQuotaOverlay` 是有意的兼容策略。仓库和产品对外名称已经统一为 Codex Desktop Companion，未来若重命名可执行文件，必须同时迁移 Mutex、启动项、卸载逻辑和旧进程识别。

## 更新影响矩阵

| 变化 | 对 Codex 的影响 | 对 Companion 的影响 |
|---|---|---|
| Companion 安装/升级 | 不改 Codex 文件，不改变额度 | 重建并替换独立运行副本 |
| Codex 常规升级 | Companion 不阻止更新 | 通常无影响 |
| Codex 左下角布局变化 | 无 | 胶囊可能需要位置适配 |
| App Server 协议变化 | 无 | 状态变为离线并重试，需要兼容修复 |
| 删除仓库 | 无 | 已安装副本继续运行，但无法从源码修复/升级 |

## UI 与动效

周额度胶囊和任务灯属于同一套紧凑密度组件，共用圆角、细边和动效节奏，但按信息类型区分状态表达：任务灯保留小面积状态灯，额度胶囊使用整块极浅绿、浅橙或浅红底色，并搭配同色系、低饱和的深色常规字重，不叠加圆点。两类详情面板均使用逐像素透明的 layered window；阴影和圆角由 `LayeredWindowRenderer` 绘制，不使用旧式窗体阴影。高频胶囊没有常驻装饰动画，只保留短按压反馈；详情面板展开 180ms、收起 120ms，动画可中断并遵循 Windows 客户区动画设置。

深色渐变桌面只属于原型展示场景，不是运行时 UI Token。真实组件必须在浅色、深色和复杂壁纸上检查边缘、对比度与阴影。
