#requires -Version 5.1
[CmdletBinding()]
param([switch]$SkipBuild,[switch]$SkipLiveVerification,[string]$UiLabPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'ui-design-lab'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'scripts\common.ps1')
$installRoot=Get-CompanionInstallRoot
$shortcutPath=Get-CompanionStartupShortcut
$manifestPath=Join-Path $installRoot 'pulse-install.json'
$previousExe=Get-CompanionInstalledExecutable
$previousManifest=if(Test-Path -LiteralPath $manifestPath){[IO.File]::ReadAllText($manifestPath)}else{$null}
$previousTask=Get-PulseBackgroundTask
$previousTaskXml=if($previousTask){$previousTask.Xml}else{$null}
$previousTaskEnabled=$previousTask -and $previousTask.Enabled
$previousPaused=Test-Path -LiteralPath (Get-PulsePausePath)
$shell=New-Object -ComObject WScript.Shell
$previousShortcut=if(Test-Path -LiteralPath $shortcutPath){$shell.CreateShortcut($shortcutPath)}else{$null}
$legacyExe=Join-Path $installRoot 'app\CodexQuotaOverlay.exe'
if($previousShortcut -and -not (Test-SamePath $previousShortcut.TargetPath $previousExe) -and -not (Test-SamePath $previousShortcut.TargetPath $legacyExe)){throw '现有启动项指向未知程序，未修改。'}
$source=Join-Path $PSScriptRoot 'bin\pulse-webview-preview'
$wasRunning=@(Get-CompanionProcesses).Count -gt 0
$switchStarted=$false
try {
    if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'prototypes\pulse-desktop\build-webview.ps1') -UiLabPath $UiLabPath}
    foreach($file in @('PulseWebPreview.exe','Microsoft.Web.WebView2.Wpf.dll','Microsoft.Web.WebView2.Core.dll','WebView2Loader.dll','renderer\index.html','renderer\LOCAL-ONLY.json')){
        if(-not (Test-Path -LiteralPath (Join-Path $source $file))){throw "缺少本机运行文件：$file"}
    }
    $release=[Guid]::NewGuid().ToString('N')
    $deployment=Join-Path $installRoot ('pulse\'+$release)
    New-Item -ItemType Directory -Path $deployment -Force | Out-Null
    Get-ChildItem -LiteralPath $source | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $deployment -Recurse -Force}
    $executable=Join-Path $deployment 'PulseWebPreview.exe'
    if((Get-FileHash -LiteralPath $executable).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source 'PulseWebPreview.exe')).Hash){throw '部署文件校验失败。'}
    # 在切换启动项前，用部署副本自测；不写真实设置、不发通知、不跳转。
    $checkPath=Join-Path $deployment 'self-test.json'
    $check=Start-Process -FilePath $executable -ArgumentList ('--live-self-test "'+$checkPath+'"') -PassThru -WindowStyle Hidden
    if(-not $check.WaitForExit(15000) -or $check.ExitCode -ne 0){throw '部署副本自测失败。'}
    # 构建和隔离自测通过后才停旧副本；切换中暂停恢复，避免旧任务抢跑。
    $switchStarted=$true
    Set-PulseBackgroundPaused $true
    if($previousTask){$previousTask.Enabled=$false}
    Stop-CompanionProcesses
    $backup=Join-Path $installRoot 'startup-before-pulse.lnk'
    if($previousShortcut -and -not (Test-Path -LiteralPath $backup)){Copy-Item -LiteralPath $shortcutPath -Destination $backup}
    $record=@{schemaVersion=1;release=$release;installedAtUtc=[DateTime]::UtcNow.ToString('o');localOnly=$true;startupMode='scheduled-task';backgroundTask='Pulse Companion'}
    [IO.File]::WriteAllText($manifestPath,($record|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    Register-PulseBackgroundTask $executable
    # 注册成功后移除已校验且有备份的旧入口，避免双重自启。
    if($previousShortcut){Remove-Item -LiteralPath $shortcutPath -ErrorAction Stop}
    & (Join-Path $PSScriptRoot 'start.ps1')
    $verification=& (Join-Path $PSScriptRoot 'verify-pulse.ps1')
    $verification
    if(-not $SkipLiveVerification -and ($verification.quotaState -ne 'ready' -or $verification.tasksState -ne 'ready')){Write-Host '后台已就绪；真实数据将在 Codex 打开并登录后读取，可再运行 verify-pulse.ps1 -StrictLive。'}
    Write-Host "Pulse Companion 已安装，Windows 计划任务负责登录启动与意外退出恢复：$deployment"
    Write-Host '旧程序及关注设置保留。回退：.\rollback-pulse.ps1。机器人资源仅本机，不得公开分发。'
} catch {
    $failure=$_
    if($switchStarted){
        Set-PulseBackgroundPaused $true
        $failedTask=Get-PulseBackgroundTask
        if($failedTask){$failedTask.Enabled=$false}
        Stop-CompanionProcesses
        Restore-PulseBackgroundTask $previousTaskXml
        if($previousManifest){[IO.File]::WriteAllText($manifestPath,$previousManifest)}elseif(Test-Path -LiteralPath $manifestPath){Remove-Item -LiteralPath $manifestPath}
        if($previousShortcut){$previousShortcut.Save()}
        Set-PulseBackgroundPaused $previousPaused
        if($wasRunning -and (Test-Path -LiteralPath $previousExe)){
            if($previousTaskEnabled){[void](Get-PulseBackgroundTask).Run($null)}
            elseif((Split-Path -Leaf $previousExe) -eq 'PulseWebPreview.exe'){Start-Process -FilePath $previousExe -ArgumentList '--live --background' -WindowStyle Hidden | Out-Null}
            else {Start-Process -FilePath $previousExe -WindowStyle Hidden | Out-Null}
        }
    }
    throw $failure
}
