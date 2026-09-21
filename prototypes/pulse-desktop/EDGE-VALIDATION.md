# Windows 透明边缘检查

> 历史阶段记录：下文为当时独立原型的验收范围，不代表当前运行模式或本次测试结果。现在 Pulse Companion 已接入真实业务并默认后台安装；当前入口与验证要求见 [README.md](README.md)。

范围：Pulse 独立原型的 Windows 呈现，不改 Lab 的 Pulse 图标、几何、配色或动作，也不改正式 Companion 的业务、安装和自启动。

## 2026-09-21 根因复现

用户提供的桌面截图中，黑色胶囊边缘出现阶梯。修复前以实际 Windows Region 对照 WPF 内容像素，在 100% 紧凑态复现：

- 可见像素 5732，半透明像素 204；
- GDI 区域裁掉 293 个可见像素，其中 181 个为抗锯齿半透明像素；
- 仅 `CapturePreviewAsync` 或 `RenderTargetBitmap` 看不到系统后续裁切，所以旧内容截图不足以验收桌面边缘。

| Before | After | Why |
| --- | --- | --- |
| `CreateRoundRectRgn` / 整数多边形贴着 SVG 外轮廓裁切 | 只用外扩包围框排除大块透明空白，造型由原 SVG 和逐像素 alpha 决定 | 二值 Region 不再剪掉半透明软边缘 |
| CompositionControl 默认 `UseLayoutRounding=true` | 窗口与 WebView 显式 `false` | 微软随 SDK 附带的 WPF XML 文档注明该默认值会禁用抗锯齿 |
| 只看 WebView/WPF 内容截图 | 内容与系统实际 Region 逐像素比对，加真实窗口视觉检查 | 避免内容平滑但桌面最终边缘锯齿的假通过 |

保留 6 CSS px 安全边距，包含焦点环和短暂入场位移。展开后轨道下方的大块空白仍在 Region 外；小块全透明圆角依靠 layered window 的逐像素 alpha。没有用模糊滤镜掩盖锯齿，也没有放大低清截图。

## 验证方法与范围

最后一次有效执行：2026-09-21 05:10:39 UTC，24/24 通过，所有状态 `ClippedPixels=0`；实际 WPF DPI=96，WebView `devicePixelRatio=1`。完整数值见本机构建产物 `artifacts/pulse-webview/verification.json`。验证后已按正常模式重启原型，未开启额外任务栏检查入口。

运行 `build-webview.ps1 -Verify`：

1. 在真实 WebView2/WPF 宿主中依次检查左右两侧、100/125/150/200% 应用渲染缩放、紧凑/展开/贴边，共 24 个状态。
2. 等待 750ms，使 600ms 面板入场结束；校验实际模式、方向、缩放、机器人来源和页面溢出。
3. 读取系统 `GetWindowRgn`，检查每个可见像素均在区域内；要求 `ClippedPixels=0`，有半透明像素，展开时有被排除的透明空白。区域读取失败直接失败。
4. 检查 `UseLayoutRounding` 未重新启用。报告记录实际 WPF DPI 与 WebView `devicePixelRatio`。
5. 本次新报告需晚于构建开始时间且进程正常退出，不能沿用旧报告。

应用缩放不等于调整 Windows 显示设置。执行顺序会从自由悬浮进入贴边，报告中的 `state.attached` 区分实际几何；不是每种几何的全组合覆盖。跨显示器的不同系统 DPI、所有壁纸和透明圆角的实际穿透点击仍需人工补测。

本轮真实系统窗口检查：在用户当前壁纸/桌面内容上查看了修复前后 100% 胶囊，修复后圆角平滑；并通过实际控件展开、收起详情，查看了面板圆角。检查入口 `--inspect-window` 只让该原型可被系统窗口工具选中，不改变呈现链路。

## 不相关回归项

主体构建、额度窗口选择自测、重点关注自测通过。现有 `--task-self-test` 的 `task-log-lifecycle` 返回失败；`src/TaskParser.cs` 自测使用固定的 2026-08-19 时间，生产解析包含时效判断。本轮未修改该文件、未调整系统时间，也未声称任务模块全部测试通过。

仍为固定样例；没有连接账户、发送模型请求、替换旧正式组件或提交/推送。
