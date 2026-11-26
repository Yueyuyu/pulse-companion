#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$UiLabPath='')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$contract=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'public-package.json') | ConvertFrom-Json
$version=[IO.File]::ReadAllText((Join-Path $repo 'VERSION')).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Expected a semantic product VERSION.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
$name="Pulse-Companion-$version-Windows-x64-Setup.exe"
$published=Join-Path $output $name
if(Test-Path -LiteralPath $published){throw "Output already exists: $published"}
$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$work=Join-Path $tempRoot ('pulse-public-build-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
function Copy-PayloadFile([string]$Source,[string]$Name) {
    if($contract.files -cnotcontains $Name){throw "Not in the package allowlist: $Name"}
    $target=Join-Path $stage $Name
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
    [IO.File]::Copy($Source,$target,$false)
}
try {
    $lab=Join-Path $work 'lab'
    $stage=Join-Path $work 'payload'
    $compileOutput=Join-Path $work 'installer'
    [void][IO.Directory]::CreateDirectory($stage)
    [void][IO.Directory]::CreateDirectory($compileOutput)
    $pin=$contract.labCommit
    if($UiLabPath){$objects=(Resolve-Path -LiteralPath $UiLabPath).Path}
    else {
        $objects=Join-Path $work 'lab.git'
        & git init --quiet --bare $objects
        if($LASTEXITCODE -ne 0){throw 'Cannot create isolated Lab object store.'}
        & git -C $objects fetch --quiet --depth=1 --no-tags 'https://github.com/Yueyuyu/ui-design-lab.git' $pin
        if($LASTEXITCODE -ne 0){throw 'Cannot fetch the pinned public Lab revision.'}
    }
    $actual=& git --no-replace-objects -C $objects rev-parse --verify ($pin+'^{commit}')
    if($LASTEXITCODE -ne 0 -or $actual -cne $pin){throw 'Pinned public Lab commit not found.'}
    $prefix='systems/pulse-desktop/'
    $labFiles=@('package.json','package-lock.json','LICENSE')+@(
      'desktop/account-state.js','desktop/application-state.js','desktop/index.html','desktop/live-state.js','desktop/main.jsx','desktop/use-window-layout.js','desktop/window.css',
      'foundations/tokens.css','web/PulseDock.jsx','web/Icons.jsx','web/bot-mark.css','web/components.css','web/model.js','web/useDockDismissal.js','web/useDockDrag.js',
      'web/upstream/motion.js','web/upstream/pulse.js','showcase/demo-data.js',
      'assets/antigravity.svg','assets/claude.svg','assets/cursor.svg','assets/grok.svg','assets/kimi.svg','assets/openai.svg',
      'licenses/Pulse-Apache-2.0.txt','licenses/Pulse-THIRD-PARTY-NOTICES.md' | ForEach-Object {$prefix+$_})
    $archive=Join-Path $work 'lab.zip'
    & git --no-replace-objects -C $objects archive --format=zip ('--output='+$archive) $pin -- @labFiles
    if($LASTEXITCODE -ne 0){throw 'Public Lab source export failed.'}
    Expand-Archive -LiteralPath $archive -DestinationPath $lab
    $web=Join-Path $lab ($prefix+'web')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'renderer/brand-mark.jsx') -Destination (Join-Path $web 'BotMark.jsx')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'renderer/public-dock.js') -Destination (Join-Path $web 'index.js')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'renderer/public-account-controls.jsx') -Destination (Join-Path $web 'PublicAccountControls.jsx')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'renderer/build-renderer.mjs') -Destination $lab
    Push-Location -LiteralPath $lab
    try {
        & npm.cmd ci --ignore-scripts --no-audit --no-fund --cache (Join-Path $work 'npm-cache')
        if($LASTEXITCODE -ne 0){throw 'Locked public renderer dependencies failed to install.'}
        & node ./build-renderer.mjs
        if($LASTEXITCODE -ne 0){throw 'Public renderer build or brand checks failed.'}
    } finally {Pop-Location}
    $buildRoot=Join-Path $work 'native'
    & (Join-Path $repo 'prototypes/pulse-desktop/build-webview.ps1') -HostOnly -PublicBrandHost -BuildRoot $buildRoot -PublicRendererManifest (Join-Path $lab 'public-renderer/PUBLIC-BRAND.json')
    foreach($item in @('PulseWebPreview.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll')){
        Copy-PayloadFile (Join-Path $buildRoot ('host/'+$item)) $item
    }
    foreach($item in @($contract.files | Where-Object {$_ -clike 'renderer/*'})){
        Copy-PayloadFile (Join-Path $lab ('public-renderer/'+$item.Substring(9))) $item
    }
    # 验证即将交给 Inno 的同一组实际文件；报告只写在临时目录，不进入包。
    $report=Join-Path $work 'public-host-check.json'
    $test=Start-Process -FilePath (Join-Path $stage 'PulseWebPreview.exe') -ArgumentList ('--live-self-test "'+$report+'"') -WindowStyle Hidden -PassThru
    if(!$test.WaitForExit(45000) -or $test.ExitCode -ne 0){throw 'Paired public host/renderer self-test failed.'}
    $offline=Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
    if(!$offline.passed -or $offline.checks -cnotcontains 'paired-public-renderer-production-eligibility' -or $offline.checks -cnotcontains 'public-brand-only-private-preferences-unchanged-and-third-party-opt-in' -or $offline.checks -cnotcontains 'public-binary-official-sources-only-no-private-readers'){throw 'Expected public renderer, consent or binary privacy checks missing.'}
    Write-Output ("公开宿主与实际 renderer 无窗口自测通过："+$offline.checks.Count+" 项。")
    foreach($item in @('common.ps1','pulse-background-task.ps1')){Copy-PayloadFile (Join-Path $repo ('scripts/'+$item)) ('install-tools/'+$item)}
    foreach($item in @('public-package.json','public-lifecycle.ps1')){Copy-PayloadFile (Join-Path $PSScriptRoot $item) ('install-tools/'+$item)}
    Copy-PayloadFile (Join-Path $PSScriptRoot 'NOTICE.txt') 'licenses/NOTICE.txt'
    Copy-PayloadFile (Join-Path $lab 'LICENSE') 'licenses/UI-Design-Lab-MIT.txt'
    foreach($item in @('Pulse-Apache-2.0.txt','Pulse-THIRD-PARTY-NOTICES.md')){Copy-PayloadFile (Join-Path $lab ($prefix+'licenses/'+$item)) ('licenses/'+$item)}
    foreach($item in @(@('react','React'),@('react-dom','React-DOM'),@('scheduler','Scheduler'))){
        Copy-PayloadFile (Join-Path $lab ('node_modules/'+$item[0]+'/LICENSE')) ('licenses/'+$item[1]+'-LICENSE.txt')
    }
    foreach($item in @('LICENSE','NOTICE')){Copy-PayloadFile (Join-Path $buildRoot ('webview2-sdk/'+$item+'.txt')) ('licenses/WebView2-'+$item+'.txt')}
    # NuGet 中固定的 Inno 6 原版工具只解压至本次临时目录，不要求安装编译器。
    $innoZip=Join-Path $work 'inno.zip'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.7.3/tools.innosetup.6.7.3.nupkg' -OutFile $innoZip
    if((Get-FileHash -LiteralPath $innoZip).Hash -ne 'F780898E402FF80612CC8D9FCB8C6E02932BD1CB4C900FFDAA31F9341CFB49F4'){throw 'Unexpected Inno Setup compiler checksum.'}
    Expand-Archive -LiteralPath $innoZip -DestinationPath (Join-Path $work 'inno')
    $compiler=Join-Path $work 'inno/tools/ISCC.exe'
    Copy-PayloadFile (Join-Path $work 'inno/tools/license.txt') 'licenses/Inno-Setup-LICENSE.txt'
    $files=@(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {$_.FullName.Substring($stage.Length+1).Replace('\','/')})
    if(Compare-Object ($contract.files | Sort-Object -CaseSensitive) ($files | Sort-Object -CaseSensitive) -CaseSensitive){throw 'Public payload does not exactly match its allowlist.'}
    # 每个 Inno Source 都是单个具体文件，不接受通配符、私有目录或额外的临时输出。
    $entries=foreach($item in $contract.files){
        if($item -notmatch '^[A-Za-z0-9._/-]+$' -or $item.Contains('..')){throw "Invalid allowlist entry: $item"}
        $relative=$item.Replace('/','\')
        $sub=[IO.Path]::GetDirectoryName($relative)
        $dest=if($sub){'{app}\'+$sub}else{'{app}'}
        'Source: "'+(Join-Path $stage $relative)+'"; DestDir: "'+$dest+'"; Flags: ignoreversion'
    }
    $include=Join-Path $work 'payload.iss'
    [IO.File]::WriteAllLines($include,[string[]]$entries,[Text.UTF8Encoding]::new($true))
    & $compiler '/Qp' ('/DProductVersion='+$version) ('/DDeploymentId='+$contract.deploymentId) ('/DPayloadInclude='+$include) ('/DIconFile='+(Join-Path $repo 'assets/icons/pulse-app.ico')) ('/O'+$compileOutput) (Join-Path $PSScriptRoot 'setup.iss')
    if($LASTEXITCODE -ne 0){throw 'Inno Setup compilation failed.'}
    $built=Join-Path $compileOutput $name
    if(!(Test-Path -LiteralPath $built -PathType Leaf) -or @(Get-ChildItem -LiteralPath $compileOutput -File).Count -ne 1){throw 'Unexpected setup output.'}
    [void][IO.Directory]::CreateDirectory($output)
    [IO.File]::Copy($built,$published,$false)
    Get-Item -LiteralPath $published
} finally {
    $resolved=[IO.Path]::GetFullPath($work)
    if((Split-Path -Parent $resolved) -ne $tempRoot -or (Split-Path -Leaf $resolved) -notmatch '^pulse-public-build-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup target.'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
