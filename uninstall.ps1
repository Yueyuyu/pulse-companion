#requires -Version 5.1
[CmdletBinding()]
param([switch]$RemoveSettings)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $projectRoot 'scripts\common.ps1')

$installRoot = [System.IO.Path]::GetFullPath((Get-CompanionInstallRoot)).TrimEnd('\')
$localAppData = [System.IO.Path]::GetFullPath(
    [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
).TrimEnd('\')
$shortcutPaths = @(
    Get-CompanionStartupShortcut
    Get-LegacyStartupShortcut
)

if (-not $installRoot.StartsWith($localAppData + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals((Split-Path -Leaf $installRoot), 'CodexDesktopCompanion', [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝删除未通过边界校验的安装目录：$installRoot"
}

Remove-PulseBackgroundTask
Stop-CompanionProcesses -IncludeLegacy

foreach ($shortcutPath in $shortcutPaths) {
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

if (Test-Path -LiteralPath $installRoot) {
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}

if ($RemoveSettings) {
    $settingsRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'CodexQuotaOverlay')).TrimEnd('\')
    if (-not $settingsRoot.StartsWith($localAppData + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals((Split-Path -Leaf $settingsRoot), 'CodexQuotaOverlay', [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝删除未通过边界校验的设置目录：$settingsRoot"
    }

    if (Test-Path -LiteralPath $settingsRoot) {
        Remove-Item -LiteralPath $settingsRoot -Recurse -Force
    }
    Write-Host '已卸载程序，并删除不可恢复的本机位置与置顶设置。'
}
else {
    Write-Host '已卸载程序。任务灯位置与置顶设置已保留，重装后会继续使用。'
}

Write-Host '项目源码未删除。'
