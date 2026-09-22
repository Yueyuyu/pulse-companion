#requires -Version 5.1
# Windows 负责启动与存活恢复，不从 Codex 的子进程链直接托管常驻程序。
function Get-PulsePausePath {
    return (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexQuotaOverlay\pulse-background.pause')
}

function Set-PulseBackgroundPaused {
    param([bool]$Paused)
    $path = Get-PulsePausePath
    if ($Paused) {
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        [IO.File]::WriteAllText($path, 'paused')
    } elseif (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -ErrorAction Stop
    }
}

function Get-PulseTaskFolder {
    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    return $service.GetFolder('\')
}

function Test-PulseCurrentUser {
    param([string]$Identity)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($Identity -eq $sid) { return $true }
    # Task Scheduler 会把 SID 规范化为账户名，读取后仍按 SID 核验归属。
    try {
        return ([Security.Principal.NTAccount]::new($Identity).Translate([Security.Principal.SecurityIdentifier]).Value -eq $sid)
    } catch { return $false }
}

function Assert-PulseTaskDefinition {
    param([Parameter(Mandatory=$true)]$Definition)
    if ($Definition.RegistrationInfo.Source -ne 'urn:pulse-companion:background:v1' -or
        -not (Test-PulseCurrentUser $Definition.Principal.UserId) -or $Definition.Principal.LogonType -ne 3 -or
        $Definition.Principal.RunLevel -ne 0 -or $Definition.Actions.Count -ne 1) {
        throw '同名计划任务不属于当前用户的 Pulse 后台配置，未修改。'
    }
    $action = $Definition.Actions.Item(1)
    $root = [IO.Path]::GetFullPath((Get-CompanionInstallRoot)).TrimEnd('\')
    $expected = '^' + [regex]::Escape($root) + '\\pulse\\[a-f0-9]{32}\\PulseWebPreview\.exe$'
    if ($action.Type -ne 0 -or $action.Path -notmatch $expected -or
        $action.Arguments -ne '--live --background' -or
        -not (Test-SamePath $action.WorkingDirectory (Split-Path -Parent $action.Path))) {
        throw '计划任务的程序路径或参数不在已知 Pulse 安装边界内，未修改。'
    }
}

function Get-PulseBackgroundTask {
    $folder = Get-PulseTaskFolder
    try { $task = $folder.GetTask('Pulse Companion') }
    catch {
        if ($_.Exception.HResult -eq -2147024894) { return $null }
        throw
    }
    Assert-PulseTaskDefinition $task.Definition
    return $task
}

function Assert-PulseRecoveryPolicy {
    param([Parameter(Mandatory=$true)]$Definition)
    $settings = $Definition.Settings
    $logon = @($Definition.Triggers | Where-Object { $_.Type -eq 9 -and $_.Enabled -and (Test-PulseCurrentUser $_.UserId) })
    $recovery = @($Definition.Triggers | Where-Object { $_.Type -eq 1 -and $_.Enabled -and $_.Repetition.Interval -eq 'PT1M' -and -not $_.Repetition.Duration -and -not $_.EndBoundary })
    if ($Definition.Triggers.Count -ne 2 -or $logon.Count -ne 1 -or $recovery.Count -ne 1 -or
        -not $settings.Enabled -or $settings.MultipleInstances -ne 2 -or $settings.ExecutionTimeLimit -ne 'PT0S' -or
        $settings.DisallowStartIfOnBatteries -or $settings.StopIfGoingOnBatteries -or
        -not $settings.StartWhenAvailable -or -not $settings.AllowDemandStart) {
        throw '后台计划任务的登录、恢复或单实例策略不匹配，请运行 repair.ps1。'
    }
}

function New-PulseTaskDefinition {
    param([Parameter(Mandatory=$true)][string]$Executable)
    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    $definition = $service.NewTask(0)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    # URI 注册后会被 Windows 改为任务路径；归属标记放在可保留的 Source。
    $definition.RegistrationInfo.Source = 'urn:pulse-companion:background:v1'
    $definition.RegistrationInfo.Description = 'Pulse Companion：登录启动，意外退出后一分钟内恢复；主动退出可暂停恢复。'
    $definition.Principal.UserId = $sid
    $definition.Principal.LogonType = 3 # 仅当前用户已登录的交互会话，不保存密码、不提权。
    $definition.Principal.RunLevel = 0
    $definition.Settings.MultipleInstances = 2 # IgnoreNew：运行期间不重开窗口。
    $definition.Settings.ExecutionTimeLimit = 'PT0S'
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
    $definition.Settings.StartWhenAvailable = $true
    $definition.Settings.AllowDemandStart = $true
    $definition.Settings.Enabled = $true
    $logon = $definition.Triggers.Create(9)
    $logon.UserId = $sid
    $recovery = $definition.Triggers.Create(1)
    $recovery.StartBoundary = (Get-Date).AddMinutes(1).ToString('s')
    $recovery.Repetition.Interval = 'PT1M'
    $action = $definition.Actions.Create(0)
    $action.Path = $Executable
    $action.WorkingDirectory = Split-Path -Parent $Executable
    $action.Arguments = '--live --background'
    Assert-PulseTaskDefinition $definition
    Assert-PulseRecoveryPolicy $definition
    return $definition
}

function Register-PulseBackgroundTask {
    param([Parameter(Mandatory=$true)][string]$Executable)
    [void](Get-PulseBackgroundTask) # 遇到同名未知任务必须停止，不能覆盖。
    $definition = New-PulseTaskDefinition $Executable
    $folder = Get-PulseTaskFolder
    $registered = $folder.RegisterTaskDefinition('Pulse Companion', $definition, 6, $definition.Principal.UserId, $null, 3)
    Assert-PulseTaskDefinition $registered.Definition
    Assert-PulseRecoveryPolicy $registered.Definition
}

function Remove-PulseBackgroundTask {
    $task = Get-PulseBackgroundTask
    if ($task) {
        $task.Enabled = $false
        Stop-CompanionProcesses
        $task.Stop(0)
        (Get-PulseTaskFolder).DeleteTask('Pulse Companion', 0)
    }
}

function Restore-PulseBackgroundTask {
    param([string]$Xml)
    if (-not $Xml) { Remove-PulseBackgroundTask; return }
    [void](Get-PulseBackgroundTask)
    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    $definition = $service.NewTask(0)
    $definition.XmlText = $Xml
    Assert-PulseTaskDefinition $definition
    [void](Get-PulseTaskFolder).RegisterTaskDefinition('Pulse Companion', $definition, 6, $definition.Principal.UserId, $null, 3)
}
