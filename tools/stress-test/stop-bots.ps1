<#
.SYNOPSIS
    Kill every bot process spawned by launch-bots.ps1.

.DESCRIPTION
    Reads PIDs from Builds/Bot/bots.pid (written by launch-bots) and
    terminates each one. Missing or already-dead PIDs are ignored.
#>
param(
    [string] $PidFile = "Builds/Bot/bots.pid"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectRoot = (Resolve-Path (Join-Path $scriptDir "../..")).Path
$resolvedPidFile = Join-Path $projectRoot $PidFile

if (-not (Test-Path $resolvedPidFile)) {
    Write-Host "No PID file at $resolvedPidFile - nothing to do."
    exit 0
}

$pids = Get-Content $resolvedPidFile | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ }
$killed = 0
foreach ($botPid in $pids) {
    try {
        Stop-Process -Id $botPid -Force -ErrorAction Stop
        $killed++
    } catch {
        # Process already gone - ignore.
    }
}

Remove-Item $resolvedPidFile -Force
Write-Host "Killed $killed / $($pids.Count) bot processes. PID file removed."
