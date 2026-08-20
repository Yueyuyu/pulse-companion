#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipLiveVerification
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $projectRoot 'scripts\common.ps1')

if ($env:OS -ne 'Windows_NT') {
    throw 'Codex Desktop Companion 目前只支持 Windows。'
}

$sourceOverlay = Join-Path $projectRoot 'bin\CodexQuotaOverlay.exe'
$sourceProbe = Join-Path $projectRoot 'bin\CodexQuotaProbe.exe'
$installRoot = Get-CompanionInstallRoot
$appRoot = Join-Path $installRoot 'app'
$installedOverlay = Get-CompanionInstalledExecutable
$installedProbe = Get-CompanionInstalledProbe
$shortcutPath = Get-CompanionStartupShortcut
$legacyShortcutPath = Get-LegacyStartupShortcut

# 安装前关闭旧路径和当前路径的实例，避免单实例互斥锁让新版本静默退出。
Stop-CompanionProcesses -IncludeLegacy

if (-not $SkipBuild) {
    & (Join-Path $projectRoot 'build.ps1')
}

if (-not (Test-Path -LiteralPath $sourceOverlay) -or -not (Test-Path -LiteralPath $sourceProbe)) {
    throw '缺少构建产物。请去掉 -SkipBuild 后重新安装。'
}

$null = New-Item -ItemType Directory -Path $appRoot -Force
Copy-Item -LiteralPath $sourceOverlay -Destination $installedOverlay -Force
Copy-Item -LiteralPath $sourceProbe -Destination $installedProbe -Force

$installMetadata = [ordered]@{
    schemaVersion = 1
    product = 'Codex Desktop Companion'
    sourcePath = $projectRoot
    installedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
}
$installMetadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installRoot 'install.json') -Encoding UTF8

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedOverlay
$shortcut.WorkingDirectory = $appRoot
$shortcut.Description = 'Codex 桌面伴侣：周额度与任务状态'
$shortcut.WindowStyle = 7
$shortcut.Save()

if (Test-Path -LiteralPath $legacyShortcutPath) {
    Remove-Item -LiteralPath $legacyShortcutPath -Force
}

& (Join-Path $projectRoot 'start.ps1')

if ($SkipLiveVerification) {
    & (Join-Path $projectRoot 'verify.ps1') -Offline
}
else {
    & (Join-Path $projectRoot 'verify.ps1')
}

Write-Host ''
Write-Host '安装完成。以后登录 Windows 会自动启动，无需再次安装。'
Write-Host "运行目录：$appRoot"
Write-Host "开机启动：$shortcutPath"
