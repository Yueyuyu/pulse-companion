#requires -Version 5.1
Set-StrictMode -Version 2.0

function Get-CompanionProjectRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function Get-CompanionInstallRoot {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    return (Join-Path $localAppData 'CodexDesktopCompanion')
}

function Get-CompanionInstalledExecutable {
    return (Join-Path (Get-CompanionInstallRoot) 'app\CodexQuotaOverlay.exe')
}

function Get-CompanionInstalledProbe {
    return (Join-Path (Get-CompanionInstallRoot) 'app\CodexQuotaProbe.exe')
}

function Get-CompanionStartupShortcut {
    $startupFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
    return (Join-Path $startupFolder 'Codex Desktop Companion.lnk')
}

function Get-LegacyStartupShortcut {
    $startupFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
    return (Join-Path $startupFolder 'Codex Quota Overlay.lnk')
}

function Test-SamePath {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right
    )

    try {
        $leftFullPath = [System.IO.Path]::GetFullPath($Left).TrimEnd('\')
        $rightFullPath = [System.IO.Path]::GetFullPath($Right).TrimEnd('\')
        return [string]::Equals($leftFullPath, $rightFullPath, [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Get-CompanionProcesses {
    return @(
        Get-CimInstance Win32_Process -Filter "Name = 'CodexQuotaOverlay.exe'" -ErrorAction SilentlyContinue
    )
}

function Test-ProcessMatchesPath {
    param(
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][string]$ExecutablePath
    )

    if ($Process.ExecutablePath -and (Test-SamePath -Left $Process.ExecutablePath -Right $ExecutablePath)) {
        return $true
    }

    if ($Process.CommandLine) {
        $quotedPath = '"' + [System.IO.Path]::GetFullPath($ExecutablePath) + '"'
        return $Process.CommandLine.StartsWith($quotedPath, [StringComparison]::OrdinalIgnoreCase)
    }

    return $false
}

function Stop-CompanionProcesses {
    [CmdletBinding()]
    param([switch]$IncludeLegacy)

    $projectExecutable = Join-Path (Get-CompanionProjectRoot) 'bin\CodexQuotaOverlay.exe'
    $installedExecutable = Get-CompanionInstalledExecutable
    $allProcesses = @(Get-CompanionProcesses)
    $targetProcesses = @(
        $allProcesses | Where-Object {
            $IncludeLegacy -or
            (Test-ProcessMatchesPath -Process $_ -ExecutablePath $projectExecutable) -or
            (Test-ProcessMatchesPath -Process $_ -ExecutablePath $installedExecutable)
        }
    )

    if ($targetProcesses.Count -eq 0) {
        Write-Host 'Codex 桌面伴侣当前未运行。'
        return
    }

    $targetIds = @($targetProcesses | ForEach-Object { [int]$_.ProcessId })
    $childIds = @(
        Get-CimInstance Win32_Process -Filter "Name = 'codex.exe'" -ErrorAction SilentlyContinue |
            Where-Object {
                $targetIds -contains [int]$_.ParentProcessId -and
                $_.CommandLine -match '(?i)\bapp-server\b'
            } |
            ForEach-Object { [int]$_.ProcessId }
    )

    if ($childIds.Count -gt 0) {
        Stop-Process -Id $childIds -Force -ErrorAction SilentlyContinue
    }

    Stop-Process -Id $targetIds -Force -ErrorAction SilentlyContinue
    foreach ($processId in $targetIds) {
        Wait-Process -Id $processId -Timeout 5 -ErrorAction SilentlyContinue
    }

    Write-Host "已停止 Codex 桌面伴侣进程：$($targetIds -join ', ')"
}

function Get-ShortcutTarget {
    param([Parameter(Mandatory = $true)][string]$ShortcutPath)

    if (-not (Test-Path -LiteralPath $ShortcutPath)) {
        return $null
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    return $shortcut.TargetPath
}
