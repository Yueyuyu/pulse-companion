#requires -Version 5.1
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'scripts\common.ps1')
$installRoot=Get-CompanionInstallRoot
$legacyExe=Join-Path $installRoot 'app\CodexQuotaOverlay.exe'
$backup=Join-Path $installRoot 'startup-before-pulse.lnk'
if(-not (Test-Path -LiteralPath $legacyExe) -or -not (Test-Path -LiteralPath $backup)){throw '缺少旧版或启动备份，未改变当前安装。'}
if(-not (Test-SamePath (Get-ShortcutTarget $backup) $legacyExe)){throw '备份不指向原伴侣，未自动回退。'}
Remove-PulseBackgroundTask
Stop-CompanionProcesses
Copy-Item -LiteralPath $backup -Destination (Get-CompanionStartupShortcut) -Force
$manifest=Join-Path $installRoot 'pulse-install.json'
if(Test-Path -LiteralPath $manifest){
    $archive=Join-Path $installRoot ('pulse-install-disabled-'+[Guid]::NewGuid().ToString('N')+'.json')
    Move-Item -LiteralPath $manifest -Destination $archive
}
& (Join-Path $PSScriptRoot 'start.ps1')
Write-Host '已恢复旧伴侣启动项；Pulse 部署副本、图标偏好与原关注设置均保留。'
