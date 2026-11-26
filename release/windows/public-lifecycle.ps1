#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Check','Quiesce','Activate','Recover','Start','Uninstall')][string]$Action,
      [string]$ProductVersion='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$contract=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'public-package.json') | ConvertFrom-Json
if($contract.distribution -cne 'public-brand' -or $contract.deploymentId -cnotmatch '^[a-f0-9]{32}$' -or
    @($contract.files | Where-Object {$_ -notmatch '^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$' -or $_.Contains('..')}).Count){exit 22}
$root=Get-CompanionInstallRoot
$deployment=Join-Path $root ('pulse\'+$contract.deploymentId)
$exe=Join-Path $deployment 'PulseWebPreview.exe'
$manifest=Join-Path $root 'pulse-install.json'
function Read-Record {
    if(Test-Path -LiteralPath $manifest){
        try {return (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json)}
        catch {throw 'E22: 安装记录不可验证。'}
    }
}
function Test-PublicRecord($Record) {
    return ($null -ne $Record -and $Record.PSObject.Properties.Name -contains 'localOnly' -and
        $Record.localOnly -is [bool] -and !$Record.localOnly -and $Record.PSObject.Properties.Name -contains 'distribution' -and
        $Record.distribution -ceq $contract.distribution -and $Record.schemaVersion -eq 1 -and $Record.release -ceq $contract.deploymentId)
}
function Get-OwnedTask {
    try {return Get-PulseBackgroundTask} catch {throw 'E22: 无法验证同名计划任务属于当前用户的 Pulse。'}
}
function Assert-SafePaths {
    $paths=@($root,(Join-Path $root 'pulse'),$deployment,$manifest)
    foreach($file in $contract.files){
        $p=Join-Path $deployment $file
        while($p -and !(Test-SamePath $p $deployment)){$paths+=$p;$p=Split-Path -Parent $p}
    }
    foreach($p in ($paths | Sort-Object -Unique)){
        if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'E22: 安装路径包含重解析点。'}
    }
}
function Assert-InstallOwnership([bool]$BeforeCopy) {
    $record=Read-Record
    if($record -and !(Test-PublicRecord $record)){throw 'E21: 拒绝覆盖活动的本机专用版、旧版或其他发行版。'}
    if((Test-Path -LiteralPath (Get-CompanionStartupShortcut)) -or (Test-Path -LiteralPath (Get-LegacyStartupShortcut))){throw 'E21: 现有启动快捷方式未迁移。'}
    $task=Get-OwnedTask
    if($task -and (!$record -or !(Test-SamePath $task.Definition.Actions.Item(1).Path $exe))){throw 'E21: 另一 Pulse 部署仍拥有后台入口。'}
    if(!$record -and $BeforeCopy -and (Test-Path -LiteralPath $deployment)){throw 'E22: 目标目录已存在且无公开版安装记录。'}
    if(!$record -and (Test-Path -LiteralPath (Join-Path $root 'app/CodexQuotaOverlay.exe'))){throw 'E21: 发现旧版安装。'}
}
function Assert-WebView {
    foreach($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryHive]::LocalMachine)){
        $reg=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,[Microsoft.Win32.RegistryView]::Registry32)
        try {
            $key=$reg.OpenSubKey('SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
            if($key){
                try {$installed=[version]'0.0';if([version]::TryParse([string]$key.GetValue('pv'),[ref]$installed) -and $installed.Major -gt 0){return}}
                finally {$key.Dispose()}
            }
        } finally {$reg.Dispose()}
    }
    throw 'E23: 缺少 Microsoft Edge WebView2 Evergreen Runtime。'
}
function Stop-PublicProcess($Task) {
    if($Task){
        if(!(Test-SamePath $Task.Definition.Actions.Item(1).Path $exe)){throw 'E22: 不是公开版的任务。'}
        $Task.Enabled=$false
        if($Task.State -eq 4){$Task.Stop(0)}
    }
    foreach($p in @(Get-CimInstance Win32_Process -Filter "Name='PulseWebPreview.exe'")){
        if($p.ExecutablePath -and (Test-SamePath $p.ExecutablePath $exe)){
            $proc=Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
            if($proc -and $proc.Path -and (Test-SamePath $proc.Path $exe)){
                try {$proc.Kill()} catch {if(!$proc.HasExited){throw}}
                if(!$proc.WaitForExit(10000)){throw 'E22: 公开版程序未能退出。'}
            }
        }
    }
}
function Start-IfAllowed($Task) {
    if($Task -and !(Test-Path -LiteralPath (Get-PulsePausePath))){
        [void]$Task.Run($null)
    }
}
try {
    Assert-SafePaths
    switch($Action){
        'Check' {Assert-InstallOwnership $true;Assert-WebView}
        'Quiesce' {Assert-InstallOwnership $true;Stop-PublicProcess (Get-OwnedTask)}
        'Recover' {
            if(Test-PublicRecord (Read-Record)){
                $task=Get-OwnedTask
                if($task -and (Test-SamePath $task.Definition.Actions.Item(1).Path $exe)){$task.Enabled=$true;Start-IfAllowed $task}
            }
        }
        'Activate' {
            Assert-InstallOwnership $false
            if($ProductVersion -notmatch '^\d+\.\d+\.\d+$'){throw 'E22: 无效安装版本。'}
            foreach($f in $contract.files){if(!(Test-Path -LiteralPath (Join-Path $deployment $f) -PathType Leaf)){throw 'E22: 安装文件不完整。'}}
            $prior=Read-Record
            $priorTask=Get-OwnedTask
            $xml=if($priorTask){$priorTask.Xml}else{$null}
            $temp=Join-Path $root ('pulse-public-'+[Guid]::NewGuid().ToString('N')+'.tmp')
            $record=[ordered]@{schemaVersion=1;release=$contract.deploymentId;version=$ProductVersion;installedAtUtc=[DateTime]::UtcNow.ToString('o');localOnly=$false;distribution=$contract.distribution;startupMode='scheduled-task';backgroundTask='Pulse Companion'}
            try {
                [IO.File]::WriteAllText($temp,($record | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
                Register-PulseBackgroundTask $exe
                $registered=Get-OwnedTask
                if(!$registered -or !(Test-SamePath $registered.Definition.Actions.Item(1).Path $exe)){throw 'E22: 无法核实注册结果。'}
                if($prior){[IO.File]::Replace($temp,$manifest,[System.Management.Automation.Language.NullString]::Value)}
                else {[IO.File]::Move($temp,$manifest)}
            } catch {
                if($xml){Restore-PulseBackgroundTask $xml}
                else {$t=Get-OwnedTask;if($t -and (Test-SamePath $t.Definition.Actions.Item(1).Path $exe)){(Get-PulseTaskFolder).DeleteTask('Pulse Companion',0)}}
                throw
            } finally {if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
            # 注册和记录提交后，立即运行仅是便利功能。会话不可用时向安装器明确返回已安装、暂缓启动。
            try {Start-IfAllowed $registered} catch {Write-Warning '安装已完成；本次立即启动未成功，请从开始菜单启动，或等待下次登录。';exit 10}
        }
        'Start' {
            if(!(Test-PublicRecord (Read-Record))){throw 'E21: 本公开版并非活动部署。'}
            $task=Get-OwnedTask
            if(!$task -or !(Test-SamePath $task.Definition.Actions.Item(1).Path $exe)){throw 'E22: 后台入口不属于本版。'}
            Set-PulseBackgroundPaused $false
            $task.Enabled=$true
            Start-IfAllowed $task
        }
        'Uninstall' {
            $record=Read-Record
            # 安装之后若另一个发行版已接管，不移除它的任务、记录或备份。
            if(!$record -or (Test-PublicRecord $record)){
                $task=Get-OwnedTask
                if($task -and !(Test-SamePath $task.Definition.Actions.Item(1).Path $exe)){throw 'E22: 活动任务不属于本公开版。'}
                Stop-PublicProcess $task
                if($task){(Get-PulseTaskFolder).DeleteTask('Pulse Companion',0)}
                if($record){Remove-Item -LiteralPath $manifest}
            }
        }
    }
} catch {
    $reason=$_.Exception.Message
    if($reason -match '^E(21|22|23):'){Write-Warning $reason;exit ([int]$Matches[1])}
    Write-Warning ('Pulse 安装操作失败：'+$Action+' ('+$_.Exception.GetType().Name+')');exit 1
}
exit 0
