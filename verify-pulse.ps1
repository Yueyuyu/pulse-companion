#requires -Version 5.1
[CmdletBinding()]
param([switch]$StrictLive)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'scripts\common.ps1')
$executable=Get-PulseInstalledExecutable
if(-not $executable -or -not (Test-Path -LiteralPath $executable)){throw '没有完整的 Pulse 安装。'}
$task=Get-PulseBackgroundTask
if(-not $task -or -not $task.Enabled -or -not (Test-SamePath $task.Definition.Actions.Item(1).Path $executable)){throw '后台计划任务未启用或与安装副本不匹配，请运行 repair.ps1。'}
Assert-PulseRecoveryPolicy $task.Definition
if(Test-Path -LiteralPath (Get-PulsePausePath)){throw '后台恢复已暂停，请运行 start.ps1。'}
if(Test-Path -LiteralPath (Get-CompanionStartupShortcut)){throw '发现重复的旧 Startup 入口，请运行 repair.ps1 完成迁移。'}
$running=@(Get-CompanionProcesses)
if($running.Count -ne 1 -or -not (Test-ProcessMatchesPath $running[0] $executable)){throw '伴侣实例不唯一或非安装路径。'}
if($running[0].CommandLine -notmatch '\s--live\s+--background\s*$'){throw '安装副本没有使用正常后台参数。'}
$statusPath=Join-Path $env:LOCALAPPDATA 'CodexQuotaOverlay\pulse-runtime.json'
$deadline=[DateTime]::UtcNow.AddSeconds(30)
$state=$null
do {
    try {$state=Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json}catch{$state=$null}
    $fresh=$state -and $state.pid -eq $running[0].ProcessId -and ([DateTime]::UtcNow-([DateTime]$state.updatedAtUtc).ToUniversalTime()).TotalSeconds -lt 8
    $liveReady=$fresh -and $state.quotaState -eq 'ready' -and $state.tasksState -eq 'ready'
    if($fresh -and (-not $StrictLive -or $liveReady)){break}
    Start-Sleep -Milliseconds 500
}while([DateTime]::UtcNow -lt $deadline)
if(-not $fresh -or -not $state.background -or $state.showInTaskbar){throw '后台运行状态未通过：缺少新鲜心跳或仍显示任务栏。'}
if($StrictLive -and -not $liveReady){throw '真实额度/任务尚未就绪；打开并登录 Codex 后重试。'}
Write-Output ([pscustomobject]@{passed=$true;startupMode='scheduled-task';recoveryInterval='PT1M';pid=$state.pid;background=$state.background;showInTaskbar=$state.showInTaskbar;visible=$state.visible;quotaState=$state.quotaState;tasksState=$state.tasksState;applications=$state.applications;iconMode=$state.iconMode;executable=$executable})
