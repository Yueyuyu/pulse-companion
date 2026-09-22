#requires -Version 5.1
[CmdletBinding()]
param([switch]$IncludeLegacy)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $projectRoot 'scripts\common.ps1')

$pulseTask = Get-PulseBackgroundTask
if ($pulseTask) { Set-PulseBackgroundPaused $true }
Stop-CompanionProcesses -IncludeLegacy:$IncludeLegacy
if ($pulseTask) {
    $pulseTask.Stop(0)
    Write-Host '自动恢复已暂停；运行 .\start.ps1 后恢复。'
}
