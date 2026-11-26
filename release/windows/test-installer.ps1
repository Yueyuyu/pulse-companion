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
    return Run-Quiet $setup ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG="'+$log+'"')
}
function Test-ActiveTask {
    $t=Get-PulseBackgroundTask
    if(!$t -or !$t.Enabled){return $false}
    Assert-PulseRecoveryPolicy $t.Definition
    return (Test-SamePath $t.Definition.Actions.Item(1).Path (Join-Path $deploy 'PulseWebPreview.exe'))
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
    Assert-Check ((Invoke-Setup 'fresh') -eq 0) 'fresh-install-exited-zero'
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
    Assert-Check ((Invoke-Setup 'upgrade') -eq 0) 'upgrade-same-package-exited-zero'
    $updated=Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    Assert-Check ($updated.release -ceq $active.release -and $updated.installedAtUtc -ne $active.installedAtUtc -and (Test-ActiveTask)) 'upgrade-refreshed-same-owned-deployment'
    foreach($f in $hashes.Keys){Assert-Check ((Get-FileHash -LiteralPath $f).Hash -eq $hashes[$f]) ('preserved-on-upgrade-'+(Split-Path -Leaf $f))}
    $uninstall=Join-Path $deploy 'unins000.exe'
    Assert-Check ((Run-Quiet $uninstall '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART') -eq 0) 'uninstall-exited-zero'
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    while((Test-Path -LiteralPath $uninstall) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
    Assert-Check (!(Test-Path -LiteralPath $deploy) -and !(Test-Path -LiteralPath $manifest) -and !(Test-Path -LiteralPath $shortcut) -and !(Test-Path -LiteralPath $registry) -and !(Get-PulseBackgroundTask)) 'uninstall-removed-runtime-registration-menu-and-task'
    foreach($f in $hashes.Keys){Assert-Check ((Get-FileHash -LiteralPath $f).Hash -eq $hashes[$f]) ('preserved-on-uninstall-'+(Split-Path -Leaf $f))}
    $success=$true
} finally {
    $report=[IO.Path]::GetFullPath($ReportPath)
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $report))
    [IO.File]::WriteAllText($report,([ordered]@{passed=$success;checks=$checks;version=$version;installerSha256=(Get-FileHash -LiteralPath $setup).Hash;verifiedAtUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
}
Write-Output "Windows 安装、升级、卸载隔离验收通过：$report"
