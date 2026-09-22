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
# 检查窗口期间暂停后台恢复；检查结束后用 start.ps1 恢复正常后台。
$backgroundTask=Get-PulseBackgroundTask
$previousPaused=Test-Path -LiteralPath (Get-PulsePausePath)
if($backgroundTask){Set-PulseBackgroundPaused $true}
try {
  Stop-CompanionProcesses
  $pulseArgs=@('--live');if($InspectWindow){$pulseArgs+='--inspect-window'}
  $pulseProcess=Start-Process -FilePath $pulseExe -ArgumentList $pulseArgs -PassThru -WindowStyle Hidden
  if($pulseProcess.WaitForExit(1500)){throw "Pulse 检查窗口启动失败（退出码 $($pulseProcess.ExitCode)）。"}
} catch {
  if($backgroundTask){
    Set-PulseBackgroundPaused $previousPaused
    if(-not $previousPaused){& (Join-Path $pulseRoot 'start.ps1')}
  } elseif($running.Count -gt 0 -and (Test-Path -LiteralPath $legacyExe)){
    Start-Process -FilePath $legacyExe -WindowStyle Hidden | Out-Null
  }
  throw
}
Write-Output "Pulse Companion 检查窗口已启动（PID $($pulseProcess.Id)）；正常后台恢复已暂停。检查结束后运行 stop.ps1，再运行 start.ps1。"
