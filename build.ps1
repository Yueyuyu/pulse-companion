#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot 'src'
$outputRoot = Join-Path $projectRoot 'bin'
$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
    throw '未找到 Windows 自带的 .NET Framework C# 编译器。请先启用 .NET Framework 4.x。'
}

if (-not (Test-Path -LiteralPath $sourceRoot)) {
    throw "源码目录不存在：$sourceRoot"
}

if (-not (Test-Path -LiteralPath $outputRoot)) {
    $null = New-Item -ItemType Directory -Path $outputRoot
}

$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) {
    throw '没有找到 C# 源文件。'
}

$references = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll'
)

$commonArguments = @(
    '/nologo',
    '/optimize+',
    '/platform:x64',
    '/warn:4',
    '/codepage:65001',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest'))
) + $references + $sources

$overlayPath = Join-Path $outputRoot 'CodexQuotaOverlay.exe'
$probePath = Join-Path $outputRoot 'CodexQuotaProbe.exe'

& $compiler @('/target:winexe', ('/out:' + $overlayPath)) @commonArguments
if ($LASTEXITCODE -ne 0) {
    throw "桌面伴侣编译失败，退出码：$LASTEXITCODE"
}

& $compiler @('/target:exe', '/define:CONSOLE', ('/out:' + $probePath)) @commonArguments
if ($LASTEXITCODE -ne 0) {
    throw "验证工具编译失败，退出码：$LASTEXITCODE"
}

Write-Host "构建完成：$overlayPath"
Write-Host "构建完成：$probePath"
