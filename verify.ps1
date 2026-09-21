#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Development,
    [switch]$Offline,
    [switch]$StrictLive
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $projectRoot 'scripts\common.ps1')

if (-not $Development -and (Get-PulseInstalledExecutable)) {
    & (Join-Path $projectRoot 'verify-pulse.ps1') -StrictLive:($StrictLive -and -not $Offline)
    return
}

function Invoke-CompanionProbe {
    param(
        [Parameter(Mandatory = $true)][string]$ProbePath,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$Required
    )

    $output = @(& $ProbePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).Trim()
    if ($exitCode -eq 0) {
        Write-Host "[通过] $Name：$text"
        return $true
    }

    if ($Required) {
        throw "[失败] $Name：$text"
    }

    Write-Warning "$Name 暂不可用：$text"
    return $false
}

if ($Development) {
    $overlayPath = Join-Path $projectRoot 'bin\CodexQuotaOverlay.exe'
    $probePath = Join-Path $projectRoot 'bin\CodexQuotaProbe.exe'
}
else {
    $overlayPath = Get-CompanionInstalledExecutable
    $probePath = Get-CompanionInstalledProbe
}

foreach ($requiredFile in @($overlayPath, $probePath)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "缺少文件：$requiredFile"
    }
}

[void](Invoke-CompanionProbe -ProbePath $probePath -Name '额度解析自测' -Arguments @('--self-test') -Required)
[void](Invoke-CompanionProbe -ProbePath $probePath -Name '任务解析自测' -Arguments @('--task-self-test') -Required)
[void](Invoke-CompanionProbe -ProbePath $probePath -Name 'Codex 任务深链自测' -Arguments @('--thread-uri-self-test') -Required)
[void](Invoke-CompanionProbe -ProbePath $probePath -Name '重点关注生命周期自测' -Arguments @('--watch-self-test') -Required)

if (-not $Development) {
    $shortcutPath = Get-CompanionStartupShortcut
    $shortcutTarget = Get-ShortcutTarget -ShortcutPath $shortcutPath
    if (-not $shortcutTarget -or -not (Test-SamePath -Left $shortcutTarget -Right $overlayPath)) {
        throw "开机启动快捷方式不正确：$shortcutPath"
    }
    Write-Host "[通过] 开机启动指向独立安装目录：$shortcutTarget"

    $allProcesses = @(Get-CompanionProcesses)
    $installedProcesses = @($allProcesses | Where-Object { Test-ProcessMatchesPath -Process $_ -ExecutablePath $overlayPath })
    if ($allProcesses.Count -ne 1 -or $installedProcesses.Count -ne 1) {
        throw "运行实例不唯一或路径不正确。总数：$($allProcesses.Count)，安装路径实例：$($installedProcesses.Count)"
    }
    Write-Host "[通过] 唯一运行实例：PID $($installedProcesses[0].ProcessId)"

    [void](Invoke-CompanionProbe -ProbePath $probePath -Name '任务灯窗口' -Arguments @('--task-window-probe') -Required)
}

if (-not $Offline) {
    [void](Invoke-CompanionProbe -ProbePath $probePath -Name 'Codex 路径发现' -Arguments @('--codex-path-probe') -Required:$StrictLive)
    [void](Invoke-CompanionProbe -ProbePath $probePath -Name '实时周额度' -Arguments @('--probe') -Required:$StrictLive)
    [void](Invoke-CompanionProbe -ProbePath $probePath -Name '实时任务状态' -Arguments @('--task-probe') -Required:$StrictLive)
    [void](Invoke-CompanionProbe -ProbePath $probePath -Name 'Codex 窗口定位' -Arguments @('--window-probe'))
}

Write-Host ''
if ($Offline) {
    Write-Host '离线核心验证完成。未检查 Codex 登录、实时额度和实时任务数据。'
}
else {
    Write-Host '安装与运行验证完成。实时项如果显示警告，不会影响 Codex 本体，可在登录 Codex 后重试。'
}
