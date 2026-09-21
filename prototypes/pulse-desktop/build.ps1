#requires -Version 5.1
[CmdletBinding()]
param([switch]$Run, [switch]$Verify, [switch]$Legacy, [string]$UiLabPath = '')
$ErrorActionPreference = 'Stop'
if (!$Legacy) {
  if (!$UiLabPath) { $UiLabPath = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '..\ui-design-lab' }
  & (Join-Path $PSScriptRoot 'build-webview.ps1') -Run:$Run -Verify:$Verify -UiLabPath $UiLabPath
  return
}
$prototypeRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $prototypeRoot '..\..')).Path
$outputRoot = Join-Path $repoRoot 'bin\pulse-desktop-preview'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkRoot = Split-Path -Parent $compiler
$wpfRoot = Join-Path $frameworkRoot 'WPF'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows .NET Framework 4.x。' }
if ($UiLabPath) {
  $canonical = Join-Path $UiLabPath 'systems\pulse-desktop\foundations\tokens.json'
  $sourceTokens = Get-Content -LiteralPath $canonical -Raw | ConvertFrom-Json | ConvertTo-Json -Depth 12 -Compress
  $nativeTokens = Get-Content -LiteralPath (Join-Path $prototypeRoot 'tokens.json') -Raw | ConvertFrom-Json | ConvertTo-Json -Depth 12 -Compress
  if ($sourceTokens -cne $nativeTokens) { throw '原生 Token 快照与 UI Design Lab 不一致；先审查同步，不能带着漂移构建。' }
}
$null = New-Item -ItemType Directory -Force -Path $outputRoot
$sources = @(Get-ChildItem -LiteralPath $prototypeRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $wpfRoot $_) }
$executable = Join-Path $outputRoot 'PulseDesktopPreview.exe'
& $compiler '/nologo' '/target:winexe' '/platform:x64' '/codepage:65001' '/warn:4' ('/out:' + $executable) ('/win32manifest:' + (Join-Path $prototypeRoot 'preview.manifest')) @references @sources
if ($LASTEXITCODE -ne 0) { throw '原生设计验证窗口编译失败。' }
Copy-Item -LiteralPath (Join-Path $prototypeRoot 'tokens.json') -Destination (Join-Path $outputRoot 'tokens.json') -Force
Write-Output "已构建独立预览：$executable"
if ($Verify) {
  $captureRoot = Join-Path $repoRoot 'artifacts\pulse-desktop'
  $null = New-Item -ItemType Directory -Force -Path $captureRoot
  $verificationStarted = [DateTime]::UtcNow
  $process = Start-Process -FilePath $executable -ArgumentList ('--verify "' + $captureRoot + '"') -PassThru
  if (!$process.WaitForExit(55000)) { throw '原生验收尚未完成，请检查预览窗口；未终止其他进程。' }
  if ($process.ExitCode -ne 0) { throw "原生验收失败：$($process.ExitCode)" }
  $verificationReport = Get-Content -LiteralPath (Join-Path $captureRoot 'verification.json') -Raw
  $verificationResult = $verificationReport | ConvertFrom-Json
  if (!$verificationResult.passed -or ([DateTime]$verificationResult.verifiedAtUtc).ToUniversalTime() -lt $verificationStarted) { throw '本次未生成新的通过报告，拒绝沿用旧验收结果。' }
  Write-Output $verificationReport
}
if ($Run) { Start-Process -FilePath $executable | Out-Null }
