# Pulse Windows 应用与托盘图标

原始文件 `upstream/pulse-icon.svg` 来自 [Pulse 的固定提交](https://github.com/qunqin24/Pulse/blob/2e17225ece661138de9ce9c73b322c7c1b16d753/AppIcon/pulse-icon.svg)，原作者 qunqin24 / Pulse contributors，按 [Apache-2.0](../../docs/licenses/Pulse-Apache-2.0.txt) 保留来源。不包含机器人第三方素材，不代表上游官方 Windows 版本或商标授权。

2026-09-22 修改：只做 Windows 像素尺寸适配，保留原版黑色圆角底、橙黄渐变圆环及白色脉冲线。托盘保持静态，不随任务/额度闪烁。

- `pulse-app.ico`：16、20、24、32、40、48、64、128、256px。48px 及以上保留原版画幅/投影；40px 及以下使用小尺寸适配。作为 EXE 的原生图标资源，快捷方式无需外部图片路径。
- `pulse-tray.ico`：16、20、24、28、32、40、48、64px。去掉 macOS 投影、收窄外围留白，并将细线补偿到至少 1.1px、圆环至少 1.4px。每个尺寸直接从矢量源渲染，而不是放大 16px 位图。
- 托盘资源内嵌 EXE。按 Windows 主任务栏 DPI 选择实际像素尺寸，显示设置变化后重新选择，不依赖浮条所在屏幕、不改变窗口位置。
- 小尺寸帧使用带完整 alpha / AND mask 的 32-bit DIB；仅 256px 使用 PNG 帧，避免 .NET Framework 4.x 对 PNG ICO 帧的位图转换兼容问题。

普通安装/构建直接使用这两个已生成 ICO，无需安装图像依赖。只有维护图标时使用 Node.js 与开发工具 sharp（本次 0.35.4）；它负责正确渲染原 SVG 的渐变、圆角、透明度和投影，不进入 Companion 运行时。

```powershell
# 已有 sharp 时可直接运行；也支持 NODE_PATH 中的现有工具。
# 没有时只安装到被 Git 忽略的本机工具目录：
npm install --prefix .local-cache/icon-tools --no-save --package-lock=false sharp@0.35.4
node scripts/build-pulse-icons.cjs
```

检查板、各尺寸 PNG 与生成摘要在 `artifacts/pulse-icons/`，不提交个人桌面截图。更新时同时提交 SVG、生成脚本、两份 ICO，检查浅/深背景的原生尺寸及 `--live-self-test` 图标资源检查。原始 SVG 的上游注释保留，适配规则集中在生成脚本，不改写另一套标志。
