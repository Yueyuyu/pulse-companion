#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallerPath,[string]$ReportPath='')
$ErrorActionPreference='Stop'
# 此测试会执行安装/卸载。即使传入 -ReportPath，也绝不能在开发者电脑或自托管 runner 上运行。
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows' -or !$env:RUNNER_TEMP){
    throw 'Installer lifecycle tests require a disposable GitHub-hosted Windows runner.'
}
$setup=(Resolve-Path -LiteralPath $InstallerPath).Path
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$expected=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'public-package.json') | ConvertFrom-Json
$version=[IO.File]::ReadAllText((Join-Path $repo 'VERSION')).Trim()
if((Split-Path -Leaf $setup) -cne "Pulse-Companion-$version-Windows-x64-Setup.exe"){throw 'Unexpected installer filename.'}
. (Join-Path $repo 'scripts/common.ps1')
$root=Get-CompanionInstallRoot
$settings=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexQuotaOverlay'
$menu=Join-Path ([Environment]::GetFolderPath('Programs')) 'Pulse Companion'
$manifest=Join-Path $root 'pulse-install.json'
$deploy=Join-Path $root ('pulse/'+$expected.deploymentId)
$registry='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PulseCompanion.PublicBrand_is1'
foreach($target in @($root,$settings,$menu,$registry,(Get-CompanionStartupShortcut),(Get-LegacyStartupShortcut))){
    if(Test-Path -LiteralPath $target){throw 'Runner already contains an installation or personal settings. Refusing test.'}
}
if(Get-PulseBackgroundTask){throw 'Runner already has a Pulse task.'}
$scratch=Join-Path $env:RUNNER_TEMP ('pulse-installer-test-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
if(!$ReportPath){$ReportPath=Join-Path $scratch 'installer-report.json'}
$checks=[Collections.Generic.List[string]]::new()
function Assert-Check([bool]$Condition,[string]$Name){if(!$Condition){throw "Installer assertion failed: $Name"};$checks.Add($Name)}
function Run-Quiet([string]$Program,[string]$Arguments){
    $p=Start-Process -FilePath $Program -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if(!$p.WaitForExit(120000)){throw 'Installer child timed out.'}
    return $p.ExitCode
}
function Invoke-Setup([string]$Phase) {
    $log=Join-Path $scratch ($Phase+'.log')
    $code=Run-Quiet $setup ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG="'+$log+'"')
    # 只输出自有阶段名和退出码，不上传完整 Inno 日志、账户信息或本机路径。
    $logText=Get-Content -Raw -LiteralPath $log
    $steps=[regex]::Matches($logText,'Pulse lifecycle: (Check|Quiesce|Activate|Recover) -> (-?\d+)') |
        ForEach-Object {$_.Groups[1].Value+'='+$_.Groups[2].Value}
    $proof=[regex]::Matches($logText,'Pulse repair proof: ([a-z-]+)') | ForEach-Object {$_.Groups[1].Value}
    Write-Host ("installer-phase={0} exit={1} lifecycle=[{2}] repair-proof=[{3}]" -f $Phase,$code,($steps -join ','),($proof -join ','))
    return $code
}
function Invoke-SetupDenyingActiveRecord([string]$Phase) {
    # 只拒绝在新建的 runner 测试根本身创建文件，不向子目录继承；无需生产故障开关。
    $originalAcl=Get-Acl -LiteralPath $root
    $deniedAcl=Get-Acl -LiteralPath $root
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $deny=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::CreateFiles,
        [Security.AccessControl.InheritanceFlags]::None,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny)
    $deniedAcl.AddAccessRule($deny)
    try {
        Set-Acl -LiteralPath $root -AclObject $deniedAcl
        return Invoke-Setup $Phase
    } finally {Set-Acl -LiteralPath $root -AclObject $originalAcl}
}
function Test-ActiveTask {
    $t=Get-PulseBackgroundTask
    if(!$t -or !$t.Enabled){return $false}
    Assert-PulseRecoveryPolicy $t.Definition
    return (Test-SamePath $t.Definition.Actions.Item(1).Path (Join-Path $deploy 'PulseWebPreview.exe'))
}
function Test-Uninstalled {
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    while((Test-Path -LiteralPath (Join-Path $deploy 'unins000.exe')) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
    return (!(Test-Path -LiteralPath $deploy) -and !(Test-Path -LiteralPath $manifest) -and !(Test-Path -LiteralPath (Join-Path $menu 'Pulse Companion.lnk')) -and !(Test-Path -LiteralPath $registry) -and !(Get-PulseBackgroundTask))
}
$success=$false
try {
    [void][IO.Directory]::CreateDirectory($root)
    [void][IO.Directory]::CreateDirectory($settings)
    $fixture='{"schemaVersion":1,"release":"11111111111111111111111111111111","localOnly":true}'
    [IO.File]::WriteAllText($manifest,$fixture)
    $backup=Join-Path $root 'startup-before-pulse.lnk'
    $appearance=Join-Path $settings 'pulse-applications.json'
    $watch=Join-Path $settings 'task-light.json'
    $pause=Get-PulsePausePath
    foreach($file in @($backup,$watch,$pause)){[IO.File]::WriteAllText($file,'synthetic-preserve')}
    [IO.File]::WriteAllText($appearance,'{"Version":1,"Applications":{"codex":"robot"}}')
    $hashes=@{}
    foreach($file in @($backup,$watch,$pause,$appearance)){$hashes[$file]=(Get-FileHash -LiteralPath $file).Hash}
    $refusal=Invoke-Setup 'refuse-local-only'
    Assert-Check ($refusal -ne 0 -and [IO.File]::ReadAllText($manifest) -ceq $fixture -and !(Test-Path -LiteralPath $deploy) -and !(Get-PulseBackgroundTask)) 'refuse-active-local-install-without-changing-it'
    Assert-Check ((Get-Content -Raw -LiteralPath (Join-Path $scratch 'refuse-local-only.log')) -match 'Pulse lifecycle: Check -> 21') 'refusal-is-ownership-check'
    Remove-Item -LiteralPath $manifest # 仅移除本脚本创建且已验证内容的合成冲突记录。
    # 只有目录、甚至有同名元数据也不能冒充 Inno 登记过的副本。只清理本测试自己创建的空目录/具体文件。
    $unknownTools=Join-Path $deploy 'install-tools'
    [void][IO.Directory]::CreateDirectory($unknownTools)
    $lookalike=Join-Path $unknownTools 'public-package.json'
    [IO.File]::Copy((Join-Path $PSScriptRoot 'public-package.json'),$lookalike)
    Assert-Check ((Invoke-Setup 'refuse-unregistered-folder') -ne 0 -and (Test-Path -LiteralPath $lookalike) -and !(Test-Path -LiteralPath $registry)) 'unregistered-lookalike-directory-remains-untouched'
    Assert-Check ((Get-Content -Raw -LiteralPath (Join-Path $scratch 'refuse-unregistered-folder.log')) -match 'Pulse lifecycle: Check -> 22') 'unregistered-directory-is-not-eligible-for-repair'
    [IO.File]::Delete($lookalike)
    [IO.Directory]::Delete($unknownTools,$false)
    [IO.Directory]::Delete($deploy,$false)
    $failed=Invoke-SetupDenyingActiveRecord 'first-activation-denied'
    $failureLog=Get-Content -Raw -LiteralPath (Join-Path $scratch 'first-activation-denied.log')
    Assert-Check ($failed -eq 30 -and $failureLog -match 'Pulse lifecycle: Check -> 0' -and $failureLog -match 'Pulse lifecycle: Activate -> 1\b') 'post-install-activation-failure-is-not-reported-as-success'
    Assert-Check (!(Test-Path -LiteralPath $manifest) -and !(Get-PulseBackgroundTask) -and (Test-Path -LiteralPath $registry) -and (Test-Path -LiteralPath (Join-Path $deploy 'unins000.exe'))) 'failed-first-activation-leaves-only-a-registered-repairable-package'
    # 逐项核对真实 Inno 登记，而不是自行伪造一个必定能被宿主接受的登记。
    $registeredPackage=Get-Item -LiteralPath $registry
    foreach($field in @('Inno Setup: App Path','InstallLocation')){
        $location=[string]$registeredPackage.GetValue($field)
        Assert-Check ($location -and (Test-SamePath $location $deploy)) ('failed-activation-registers-canonical-'+$field)
    }
    $registeredCommand=[string]$registeredPackage.GetValue('UninstallString')
    Assert-Check ($registeredCommand -match '^"([^"]+)"(?: /LOG)?$') 'failed-activation-registers-quoted-uninstaller'
    Assert-Check (Test-SamePath $Matches[1] (Join-Path $deploy 'unins000.exe')) 'failed-activation-registers-owned-uninstaller'
    foreach($file in $expected.files){Assert-Check (Test-Path -LiteralPath (Join-Path $deploy $file) -PathType Leaf) ('failed-activation-keeps-'+$file)}
    foreach($f in $hashes.Keys){Assert-Check ((Get-FileHash -LiteralPath $f).Hash -eq $hashes[$f]) ('preserved-on-failed-activation-'+(Split-Path -Leaf $f))}
    $installedContract=Join-Path $deploy 'install-tools/public-package.json'
    $originalContract=[IO.File]::ReadAllBytes($installedContract)
    try {
        [IO.File]::AppendAllText($installedContract,' ')
        Assert-Check ((Invoke-Setup 'reject-mismatched-package') -ne 0 -and (Get-Content -Raw -LiteralPath (Join-Path $scratch 'reject-mismatched-package.log')) -match 'Pulse lifecycle: Check -> 22') 'inno-registration-alone-cannot-authorize-a-mismatched-package'
        $sameSize=[byte[]]$originalContract.Clone()
        $sameSize[$sameSize.Length-1]=$sameSize[$sameSize.Length-1] -bxor 1
        [IO.File]::WriteAllBytes($installedContract,$sameSize)
        $byteRefusal=Invoke-Setup 'reject-same-size-change'
        $byteLog=Get-Content -Raw -LiteralPath (Join-Path $scratch 'reject-same-size-change.log')
        Assert-Check ($byteRefusal -ne 0 -and $byteLog -match 'Pulse lifecycle: Check -> 22' -and $byteLog -match 'Pulse repair proof: tool-bytes') 'same-length-single-byte-change-is-not-the-original-package'
    } finally {[IO.File]::WriteAllBytes($installedContract,$originalContract)}
    Assert-Check ((Invoke-Setup 'repair-activation') -eq 0) 'same-package-retry-exited-zero'
    Assert-Check ((Get-Content -Raw -LiteralPath (Join-Path $scratch 'repair-activation.log')) -match 'Pulse lifecycle: Check -> 11') 'retry-recognizes-exact-inno-owned-installation'
    $active=Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    Assert-Check ($active.distribution -ceq 'public-brand' -and !$active.localOnly -and $active.version -ceq $version -and $active.release -ceq $expected.deploymentId) 'public-active-record-and-version'
    Assert-Check (Test-ActiveTask) 'current-user-logon-one-minute-recovery-task'
    $installedFiles=@(Get-ChildItem -LiteralPath $deploy -File -Recurse | ForEach-Object {$_.FullName.Substring($deploy.Length+1).Replace('\','/')} |
        Where-Object {$_ -notmatch '^unins000\.(exe|dat|msg)$'})
    $differences=@(Compare-Object ($expected.files | Sort-Object -CaseSensitive) ($installedFiles | Sort-Object -CaseSensitive) -CaseSensitive)
    Assert-Check ($differences.Count -eq 0 -and (Get-FileHash -LiteralPath (Join-Path $deploy 'install-tools/public-package.json')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'public-package.json')).Hash) 'exact-installed-payload-and-notices'
    $installedExe=Join-Path $deploy 'PulseWebPreview.exe'
    $pe=[IO.File]::ReadAllBytes($installedExe)
    $peOffset=[BitConverter]::ToInt32($pe,60)
    Assert-Check ([BitConverter]::ToUInt16($pe,$peOffset+4) -eq 0x8664) 'installed-host-is-amd64'
    $self=Join-Path $scratch 'installed-self-test.json'
    Assert-Check ((Run-Quiet $installedExe ('--live-self-test "'+$self+'"')) -eq 0) 'installed-executable-offline-self-test'
    $result=Get-Content -Raw -LiteralPath $self | ConvertFrom-Json
    Assert-Check ($result.passed -and $result.checks -ccontains 'public-brand-only-private-preferences-unchanged-and-third-party-opt-in') 'offline-host-validates-brand-and-consent'
    Assert-Check ($result.checks -ccontains 'paired-public-renderer-production-eligibility') 'installed-renderer-passes-production-initialization-guard'
    Assert-Check ($result.checks -ccontains 'public-binary-official-sources-only-no-private-readers') 'installed-host-contains-no-private-credential-readers'
    $shortcut=Join-Path $menu 'Pulse Companion.lnk'
    $link=(New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    Assert-Check ((Test-Path -LiteralPath $shortcut) -and $link.Arguments.Contains('public-lifecycle.ps1') -and $link.Arguments.EndsWith('-Action Start') -and (Test-Path -LiteralPath $registry)) 'start-menu-and-registered-uninstaller'
    $priorRecordHash=(Get-FileHash -LiteralPath $manifest).Hash
    Assert-Check ((Invoke-SetupDenyingActiveRecord 'upgrade-activation-denied') -eq 30 -and (Get-FileHash -LiteralPath $manifest).Hash -eq $priorRecordHash -and (Test-ActiveTask)) 'failed-upgrade-retains-prior-active-record-and-recovery-task'
    foreach($f in $hashes.Keys){Assert-Check ((Get-FileHash -LiteralPath $f).Hash -eq $hashes[$f]) ('preserved-on-failed-upgrade-'+(Split-Path -Leaf $f))}
    Assert-Check ((Invoke-Setup 'upgrade') -eq 0) 'upgrade-same-package-exited-zero'
    $updated=Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    Assert-Check ($updated.release -ceq $active.release -and $updated.installedAtUtc -ne $active.installedAtUtc -and (Test-ActiveTask)) 'upgrade-refreshed-same-owned-deployment'
    foreach($f in $hashes.Keys){Assert-Check ((Get-FileHash -LiteralPath $f).Hash -eq $hashes[$f]) ('preserved-on-upgrade-'+(Split-Path -Leaf $f))}
    $uninstall=Join-Path $deploy 'unins000.exe'
    Assert-Check ((Run-Quiet $uninstall '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART') -eq 0) 'uninstall-exited-zero'
    Assert-Check (Test-Uninstalled) 'uninstall-removed-runtime-registration-menu-and-task'
    foreach($f in $hashes.Keys){Assert-Check ((Get-FileHash -LiteralPath $f).Hash -eq $hashes[$f]) ('preserved-on-uninstall-'+(Split-Path -Leaf $f))}
    # 与上面的修复场景不同，这次从没有部署、登记、菜单和任务的状态正常安装，不注入故障。
    Assert-Check ((Invoke-Setup 'fresh') -eq 0) 'fresh-install-exited-zero'
    $freshLog=Get-Content -Raw -LiteralPath (Join-Path $scratch 'fresh.log')
    $freshRecord=Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    Assert-Check ($freshLog -match 'Pulse lifecycle: Check -> 0\b' -and $freshLog -match 'Pulse lifecycle: Activate -> 0\b' -and
        $freshRecord.distribution -ceq 'public-brand' -and $freshRecord.release -ceq $expected.deploymentId -and (Test-ActiveTask)) 'clean-first-install-activated-and-registered-without-repair'
    Assert-Check ((Run-Quiet $uninstall '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART') -eq 0 -and (Test-Uninstalled)) 'clean-first-install-uninstalls-without-leftover-registration'
    foreach($f in $hashes.Keys){Assert-Check ((Get-FileHash -LiteralPath $f).Hash -eq $hashes[$f]) ('preserved-on-clean-install-and-uninstall-'+(Split-Path -Leaf $f))}
    $success=$true
} finally {
    $report=[IO.Path]::GetFullPath($ReportPath)
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $report))
    [IO.File]::WriteAllText($report,([ordered]@{passed=$success;checks=$checks;version=$version;installerSha256=(Get-FileHash -LiteralPath $setup).Hash;verifiedAtUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
}
Write-Output "Windows 安装、升级、卸载隔离验收通过：$report"
