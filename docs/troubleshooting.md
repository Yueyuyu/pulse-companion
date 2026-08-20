# 故障排查

## 一键修复

先在仓库根目录运行：

```powershell
.\repair.ps1
```

它会停止旧路径实例、重新构建、部署到稳定目录、重建开机启动项并验证。不会删除任务灯位置。

## 任务灯显示灰色“Codex 离线”

1. 确认 Codex Desktop 已安装并已登录；
2. 运行 `.\verify.ps1 -StrictLive` 查看是路径发现、额度还是任务状态失败；
3. 运行 `.\bin\CodexQuotaProbe.exe --codex-path-probe` 检查当前 `codex.exe`；
4. 如果刚完成 Codex 更新，完全退出再打开 Codex，然后重试。

不要通过写入 Codex 配置或关闭状态校验来把离线伪装成正常。

## 左下角额度胶囊遮挡官方按钮

这是 Codex 底栏布局变化，不是额度变化。记录当前 Codex 版本和截图，检查 `src/CodexWindowTracker.cs` 的定位常量，做最小位置适配后执行：

```powershell
.\repair.ps1
.\bin\CodexQuotaProbe.exe --window-probe
```

应在 Codex 主窗口存在时做真实截图验证。

## 发现多个进程或启动后没有界面

```powershell
.\stop.ps1 -IncludeLegacy
.\start.ps1
.\verify.ps1
```

安装脚本会自动清理旧项目路径运行的同名实例。不要手工复制多个 EXE 到 Startup 文件夹。

## 毛边、黑边或阴影异常

确认没有恢复 `CS_DROPSHADOW` 或 WinForms `Region` 裁切。任务灯和详情窗应继续使用 `LayeredWindowRenderer` 的逐像素透明渲染。在不同 DPI、不同壁纸和多显示器上实际检查，普通静态截图工具偶尔会漏拍 layered window，不能仅凭一次空截图判断窗口未渲染。

## SmartScreen 或杀毒软件提示

程序由本机源码即时编译，当前没有代码签名。先核对仓库来源，并可运行：

```powershell
Get-FileHash .\bin\CodexQuotaOverlay.exe -Algorithm SHA256
```

不要为了绕过提示关闭系统安全功能。公开发布后可考虑为 Release 构建增加代码签名。
