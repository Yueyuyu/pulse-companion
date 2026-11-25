#requires -Version 5.1
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $taskRoot 'prototypes\pulse-desktop\build-webview.ps1') -HostOnly
if($LASTEXITCODE -ne 0){throw 'Host compilation failed'}
$report=Join-Path $taskRoot 'bin\pulse-webview-preview\self-test.json'
$started=[DateTime]::UtcNow
$test=Start-Process -FilePath (Join-Path $taskRoot 'bin\pulse-webview-preview\PulseWebPreview.exe') -ArgumentList ('--live-self-test "'+$report+'"') -PassThru -WindowStyle Hidden
if(!$test.WaitForExit(55000)){throw 'Host self-test timed out'}
if($test.ExitCode -ne 0){throw 'Host self-test failed'}
$result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if(!$result.passed -or ([DateTime]$result.verifiedAtUtc).ToUniversalTime() -lt $started){throw 'Stale or invalid host report'}
$result | ConvertTo-Json -Depth 4
