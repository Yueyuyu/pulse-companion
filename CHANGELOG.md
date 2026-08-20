# 变更记录

本文件记录 Codex Desktop Companion 的公开版本变化。版本遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)，内容格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [Unreleased]

## [1.1.0] - 2026-08-20

### 新增

- 提供不抢焦点的右下角桌面通知，分别提示任务完成和需要处理；点击通知可直接打开对应 Codex 任务。
- 任务详情中的每一行现在都可点击，并通过经过 UUID 校验的 `codex://threads/<thread-id>` 深链跳转到对应任务。
- 增加任务灯、通知与任务跳转的开发预览和自动验证入口。

### 优化

- 按 Quiet Workspace 视觉基准重新还原任务灯与详情：使用白色紧凑表面、细状态轨、分组计数、低饱和状态标签和更清晰的层级。
- 任务灯、详情和通知集中共用颜色、字体、图标、圆角与动效令牌，避免真实组件与 UI Design Lab 再次产生视觉漂移。
- 中文字体统一为 `Microsoft YaHei UI`，功能图标优先使用 `Segoe Fluent Icons`，辅助文字与状态色达到至少 `4.5:1` 的对比度。
- 通知进入、退出、悬停和按下反馈使用短促可中断的动效，并遵循 Windows“显示动画”设置。

### 兼容

- 自绘右下角通知不可用时，仍会回退到系统托盘气泡，不影响任务状态读取。
- 保持既有安装目录、可执行文件名、单实例锁和任务灯设置目录兼容。

## [1.0.0] - 2026-08-20

### 新增

- 提供跟随 Codex 窗口的周额度胶囊，收起时只显示剩余百分比。
- 提供白色额度详情面板，可查看重置时间、立即刷新或退出额度显示。
- 提供可跨显示器拖动并保存位置的桌面任务完成状态灯。
- 汇总需处理、执行中、空闲和离线状态，并可展开查看多个任务详情。
- 提供置顶开关、每 2 秒只读刷新和任务完成/需处理 Windows 通知。
- 提供一次安装、开机自启、幂等修复、卸载和严格实时验证脚本。
- 保持与 Codex 的外置只读边界，不修改 Codex、不创建任务、不发送模型请求。

[Unreleased]: https://github.com/Yueyuyu/codex-desktop-companion/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/Yueyuyu/codex-desktop-companion/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Yueyuyu/codex-desktop-companion/releases/tag/v1.0.0
