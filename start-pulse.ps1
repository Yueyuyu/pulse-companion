#requires -Version 5.1
[CmdletBinding()]
param([switch]$InspectWindow)
$ErrorActionPreference='Stop'
$pulseRoot=$PSScriptRoot
. (Join-Path $pulseRoot 'scripts\common.ps1')
if (-not $InspectWindow -and (Get-PulseInstalledExecutable)) { & (Join-Path $pulseRoot 'start.ps1'); return }
$pulseExe=Join-Path $pulseRoot 'bin\pulse-webview-preview\PulseWebPreview.exe'
if(!(Test-Path -LiteralPath $pulseExe)){throw '请先运行 prototypes\pulse-desktop\build-webview.ps1 构建本机 Pulse。'}
$legacyExe=Join-Path $env:LOCALAPPDATA 'CodexDesktopCompanion\app\CodexQuotaOverlay.exe'
$developmentExe=Join-Path $pulseRoot 'bin\CodexQuotaOverlay.exe'
$allowedPaths=@($legacyExe,$developmentExe,$pulseExe)
if (Get-PulseInstalledExecutable) { $allowedPaths += Get-PulseInstalledExecutable }
$running=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -in @('CodexQuotaOverlay.exe','PulseWebPreview.exe')})
foreach($item in $running){
  if(!$item.ExecutablePath -or $item.ExecutablePath -notin $allowedPaths){throw '发现未知路径的伴侣进程，未停止任何程序。请确认后手动关闭。'}
}
# 只切换运行进程；共享单实例锁防止双份通知，旧安装、设置和 Startup 快捷方式都不改。
foreach($item in $running){
  $oldProcess=Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
  if($oldProcess){
    Stop-Process -Id $item.ProcessId -ErrorAction Stop
    if(!$oldProcess.WaitForExit(5000)){throw '旧伴侣尚未退出，未启动第二个实例。'}
    $oldProcess.Dispose()
  }
}
$pulseArgs=@('--live');if($InspectWindow){$pulseArgs+='--inspect-window'}
$pulseProcess=Start-Process -FilePath $pulseExe -ArgumentList $pulseArgs -PassThru -WindowStyle Hidden
if($pulseProcess.WaitForExit(1500)){
  if(Test-Path -LiteralPath $legacyExe){Start-Process -FilePath $legacyExe -WindowStyle Hidden | Out-Null}
  throw "Pulse 启动失败（退出码 $($pulseProcess.ExitCode)），已尝试恢复旧伴侣。"
}
Write-Output "Pulse Companion 实时窗口已启动（PID $($pulseProcess.Id)）。旧安装及启动项未修改。"
