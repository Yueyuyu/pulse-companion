#requires -Version 5.1
[CmdletBinding()]
param([switch]$IncludeLegacy)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $projectRoot 'scripts\common.ps1')

Stop-CompanionProcesses -IncludeLegacy:$IncludeLegacy
