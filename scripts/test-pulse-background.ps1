#requires -Version 5.1
# 只构造计划任务定义，不注册、不启动进程、不写真实设置。
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$projectRoot = Get-CompanionProjectRoot
foreach ($relative in @('install-pulse.ps1','start.ps1','stop.ps1','start-pulse.ps1','verify-pulse.ps1','rollback-pulse.ps1','uninstall.ps1','prototypes\pulse-desktop\build-webview.ps1')) {
    $tokens=$null; $parseErrors=$null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot $relative),[ref]$tokens,[ref]$parseErrors)
    if ($parseErrors.Count) { throw "PowerShell $($PSVersionTable.PSVersion) 无法解析 $relative" }
}
$fixtureExe = Join-Path (Get-CompanionInstallRoot) 'pulse\00000000000000000000000000000000\PulseWebPreview.exe'
$definition = New-PulseTaskDefinition $fixtureExe
Assert-PulseTaskDefinition $definition
Assert-PulseRecoveryPolicy $definition
$checks = @('current-user-logon-recovery-single-instance-unlimited-runtime')

function Assert-Rejected {
    param([scriptblock]$Mutation, [scriptblock]$Validation, [string]$Label)
    $candidate = New-PulseTaskDefinition $fixtureExe
    & $Mutation $candidate
    $rejected = $false
    try { & $Validation $candidate } catch { $rejected = $true }
    if (-not $rejected) { throw "未拒绝不安全的任务定义：$Label" }
}
$ownership = { param($task) Assert-PulseTaskDefinition $task }
$policy = { param($task) Assert-PulseRecoveryPolicy $task }
Assert-Rejected { param($t) $t.RegistrationInfo.Source='unknown' } $ownership '未知任务归属'
Assert-Rejected { param($t) $t.Actions.Item(1).Path='C:\Windows\notepad.exe' } $ownership '非安装目录'
Assert-Rejected { param($t) $t.Actions.Item(1).Arguments='--verify C:\Temp' } $ownership '视觉验收参数'
Assert-Rejected { param($t) $t.Principal.RunLevel=1 } $ownership '管理员权限'
Assert-Rejected { param($t) $t.Principal.LogonType=1 } $ownership '密码登录'
Assert-Rejected { param($t) $t.Actions.Item(1).WorkingDirectory='C:\Windows' } $ownership '非安装工作目录'
Assert-Rejected { param($t) $t.Actions.Create(0).Path='C:\Windows\notepad.exe' } $ownership '多动作'
Assert-Rejected { param($t) $t.Settings.MultipleInstances=0 } $policy '并行拉起'
Assert-Rejected { param($t) $t.Settings.ExecutionTimeLimit='PT72H' } $policy '运行到时被终止'
Assert-Rejected { param($t) $t.Triggers.Item(2).Repetition.Interval='PT10M' } $policy '恢复延迟'
Assert-Rejected { param($t) $t.Triggers.Item(2).Repetition.Duration='PT1H' } $policy '恢复过期'
Assert-Rejected { param($t) $t.Triggers.Item(1).Enabled=$false } $policy '禁用登录启动'
$checks += 'ownership-path-arguments-privilege-triggers-fail-closed'
[pscustomobject]@{ passed=$true; kind='offline task definitions; no task registration or windows'; checks=$checks }
