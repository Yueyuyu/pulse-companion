#requires -Version 5.1
[CmdletBinding()]
param([switch]$SkipLiveVerification)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host '正在重新构建、部署并修复开机启动项……'
if ($SkipLiveVerification) {
    & (Join-Path $projectRoot 'install.ps1') -SkipLiveVerification
}
else {
    & (Join-Path $projectRoot 'install.ps1')
}
