#requires -Version 5.1
[CmdletBinding()]
param([switch]$Development)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $projectRoot 'scripts\common.ps1')

if ($Development) {
    $executable = Join-Path $projectRoot 'bin\CodexQuotaOverlay.exe'
    if (-not (Test-Path -LiteralPath $executable)) {
        & (Join-Path $projectRoot 'build.ps1')
    }
    $workingDirectory = $projectRoot
}
else {
    $executable = Get-CompanionInstalledExecutable
    $workingDirectory = Split-Path -Parent $executable
    if (-not (Test-Path -LiteralPath $executable)) {
        throw '尚未安装。请先运行 .\install.ps1。'
    }
}

$allProcesses = @(Get-CompanionProcesses)
$matchingProcesses = @($allProcesses | Where-Object { Test-ProcessMatchesPath -Process $_ -ExecutablePath $executable })
$pulseTask = $null
if ((Split-Path -Leaf $executable) -eq 'PulseWebPreview.exe') {
    $pulseTask = Get-PulseBackgroundTask
    if (-not $pulseTask -or -not (Test-SamePath $pulseTask.Definition.Actions.Item(1).Path $executable)) {
        throw '缺少匹配的 Pulse 后台计划任务，请运行 .\repair.ps1 完成安装修复。'
    }
}
if ($matchingProcesses.Count -gt 0) {
    if ($matchingProcesses.Count -ne 1 -or $allProcesses.Count -ne 1) { throw '检测到多个伴侣实例，请先运行 stop.ps1。' }
    if ($pulseTask) {
        if ($matchingProcesses[0].CommandLine -notmatch '\s--live\s+--background\s*$') { throw '正在运行检查窗口，请先运行 stop.ps1。' }
        Set-PulseBackgroundPaused $false
        $pulseTask.Enabled = $true
    }
    Write-Host 'Pulse Companion 已经在运行。'
    return
}

if ($allProcesses.Count -gt 0) {
    $paths = @($allProcesses | ForEach-Object { if ($_.ExecutablePath) { $_.ExecutablePath } else { $_.CommandLine } })
    throw "检测到另一个路径的旧实例。请运行 .\repair.ps1 完成切换。当前实例：$($paths -join '; ')"
}

if ((Split-Path -Leaf $executable) -eq 'PulseWebPreview.exe') {
    Set-PulseBackgroundPaused $false
    $pulseTask.Enabled = $true
    [void]$pulseTask.Run($null)
} else {
    Start-Process -FilePath $executable -WorkingDirectory $workingDirectory -WindowStyle Hidden
}

$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 200
    $matchingProcesses = @(Get-CompanionProcesses | Where-Object { Test-ProcessMatchesPath -Process $_ -ExecutablePath $executable })
} while ($matchingProcesses.Count -eq 0 -and (Get-Date) -lt $deadline)

if ($matchingProcesses.Count -ne 1) {
    throw '启动后未检测到唯一的 Pulse Companion 进程。'
}

Write-Host "Pulse Companion 已启动，进程 ID：$($matchingProcesses[0].ProcessId)"
