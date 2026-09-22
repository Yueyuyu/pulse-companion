#requires -Version 5.1
[CmdletBinding()]
param([switch]$Run,[switch]$Verify,[switch]$VerifyLive,[switch]$Live,[switch]$InspectWindow,[string]$UiLabPath='')
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($UiLabPath)){$UiLabPath=Join-Path $PSScriptRoot '..\..\..\ui-design-lab'}
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputRoot=Join-Path $repoRoot 'bin\pulse-webview-preview'
$sdkRoot=Join-Path $repoRoot 'bin\webview2-sdk'
$sdkVersion='1.0.4191.47'
if(!(Test-Path -LiteralPath (Join-Path $sdkRoot 'lib\net462\Microsoft.Web.WebView2.Wpf.dll'))) {
  New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null
  $archive=Join-Path $sdkRoot 'sdk.zip'
  Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg" -OutFile $archive
  Expand-Archive -LiteralPath $archive -DestinationPath $sdkRoot -Force
}
Push-Location -LiteralPath $UiLabPath
try { & node scripts/build-pulse-desktop.mjs; if($LASTEXITCODE -ne 0){throw '共享界面构建失败。'} } finally {Pop-Location}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkRoot=Split-Path -Parent $compiler
$wpfRoot=Join-Path $frameworkRoot 'WPF'
$references=@('System.dll','System.Core.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Web.dll','System.Net.Http.dll','System.Security.dll') | ForEach-Object {'/reference:'+(Join-Path $frameworkRoot $_)}
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object {'/reference:'+(Join-Path $wpfRoot $_)}
$references+=@('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll') | ForEach-Object {'/reference:'+(Join-Path $sdkRoot ('lib\net462\'+$_))}
$sources=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'webview') -Filter '*.cs' | ForEach-Object {$_.FullName}
$sources+=Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object {$_.FullName}
$executable=Join-Path $outputRoot 'PulseWebPreview.exe'
$appIcon=Join-Path $repoRoot 'assets\icons\pulse-app.ico'
$trayIcon=Join-Path $repoRoot 'assets\icons\pulse-tray.ico'
foreach($icon in @($appIcon,$trayIcon)){if(!(Test-Path -LiteralPath $icon)){throw "缺少 Pulse 图标：$icon"}}
& $compiler /nologo /target:winexe /main:CodexCompanion.PulseWebPreview.Program /platform:x64 /codepage:65001 /warn:4 ('/out:'+$executable) ('/win32manifest:'+(Join-Path $PSScriptRoot 'preview.manifest')) ('/win32icon:'+$appIcon) ('/resource:'+$trayIcon+',PulseCompanion.TrayIcon') @references @sources
if($LASTEXITCODE -ne 0){throw 'Windows WebView2 宿主编译失败。'}
Copy-Item -Path (Join-Path $sdkRoot 'lib\net462\*.dll') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $sdkRoot 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $outputRoot -Force
$renderer=Join-Path $outputRoot 'renderer'
New-Item -ItemType Directory -Force -Path $renderer | Out-Null
Copy-Item -Path (Join-Path $UiLabPath '.local-cache\pulse-desktop\*') -Destination $renderer -Recurse -Force
Write-Output "已构建 Pulse Companion：$executable（仅限本机，不可公开分发；未更新已安装副本）"
if($Verify){
  $captureRoot=Join-Path $repoRoot 'artifacts\pulse-webview'
  New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null
  $verificationStarted=[DateTime]::UtcNow
  $process=Start-Process -FilePath $executable -ArgumentList ('--verify "'+$captureRoot+'"') -PassThru -WindowStyle Hidden
  if(!$process.WaitForExit(55000)){throw '本次窗口验收超时，未终止其他程序。'}
  if($process.ExitCode -ne 0){throw "窗口验收失败：$($process.ExitCode)。请查看 artifacts/pulse-webview/error.txt"}
  $report=Get-Content -LiteralPath (Join-Path $captureRoot 'verification.json') -Raw | ConvertFrom-Json
  if(!$report.passed -or ([DateTime]$report.verifiedAtUtc).ToUniversalTime() -lt $verificationStarted){throw '拒绝沿用旧验收报告。'}
  Write-Output ($report | ConvertTo-Json -Depth 8)
}
if($VerifyLive){
  foreach($mode in @('live-self-test','verify-live')) {
    $liveReport=Join-Path $repoRoot ('artifacts\pulse-webview\'+$mode+'.json')
    $started=[DateTime]::UtcNow
    $test=Start-Process -FilePath $executable -ArgumentList ('--'+$mode+' "'+$liveReport+'"') -PassThru -WindowStyle Hidden
    if(!$test.WaitForExit(55000)){throw "$mode 超时，未沿用旧结果"}
    if($test.ExitCode -ne 0){throw "$mode 失败，请查看 $liveReport"}
    $result=Get-Content -Raw -LiteralPath $liveReport | ConvertFrom-Json
    if(!$result.passed -or ([DateTime]$result.verifiedAtUtc).ToUniversalTime() -lt $started){throw "$mode 无有效新报告"}
    $result | ConvertTo-Json -Depth 4
  }
}
if($Run){
  $runtimeArgs=@();if($Live){$runtimeArgs+='--live'}else{$runtimeArgs+='--demo'};if($InspectWindow){$runtimeArgs+='--inspect-window'}
  if($runtimeArgs.Count){Start-Process -FilePath $executable -ArgumentList $runtimeArgs -WindowStyle Hidden | Out-Null}
  else {Start-Process -FilePath $executable -WindowStyle Hidden | Out-Null}
}
