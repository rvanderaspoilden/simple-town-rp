<#
.SYNOPSIS
    Launch N headless Unity bot processes against the simple-town-ws server.

.DESCRIPTION
    Each bot is a separate Unity process built in headless / batchmode and
    is identified by --bot-index. Bots auto-provision themselves on first
    launch via POST /auth/register-bot, then loop random NavMesh moves
    in the City scene to stress the server.

    Save the PIDs to a file so stop-bots.ps1 can kill them later.

.PARAMETER Count
    Number of bots to launch.

.PARAMETER StartIndex
    First bot index. Useful to add more bots to a running set without
    colliding with existing indices.

.PARAMETER BuildPath
    Path to the built Simple Town.exe. Default: Builds/Bot/Simple Town.exe

.PARAMETER Server
    Backend URI the bots authenticate against.
    Must match SimpleTownNetwork.networkAddress.

.PARAMETER Secret
    Shared secret for /auth/register-bot. Read from
    $env:BOT_REGISTRATION_SECRET by default.

.PARAMETER StaggerMs
    Delay between successive bot launches, in milliseconds, to avoid
    thundering-herd auth/DB load.

.EXAMPLE
    ./launch-bots.ps1 -Count 10

.EXAMPLE
    ./launch-bots.ps1 -Count 50 -Server http://192.168.1.42:3000 -StaggerMs 500
#>
param(
    [int]    $Count      = 10,
    [int]    $StartIndex = 0,
    [string] $BuildPath  = "Builds/Bot/Simple Town.exe",
    [string] $Server     = "http://localhost:3000",
    [string] $Secret     = $env:BOT_REGISTRATION_SECRET,
    [int]    $StaggerMs  = 200
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($Secret)) {
    Write-Error "Missing bot secret. Pass -Secret or set BOT_REGISTRATION_SECRET."
    exit 1
}

# Resolve paths relative to the script directory so the script works no
# matter where the caller is.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectRoot = (Resolve-Path (Join-Path $scriptDir "../..")).Path
$resolvedBuild = Join-Path $projectRoot $BuildPath
if (-not (Test-Path $resolvedBuild)) {
    Write-Error "Build not found at $resolvedBuild. Build the 'Bot Headless' profile first."
    exit 1
}

$logDir = Join-Path (Split-Path $resolvedBuild) "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$pidFile = Join-Path (Split-Path $resolvedBuild) "bots.pid"
$pids    = @()

Write-Host "Launching $Count bots, indices [$StartIndex..$($StartIndex + $Count - 1)] -> $Server"

for ($i = 0; $i -lt $Count; $i++) {
    $index   = $StartIndex + $i
    $logPath = Join-Path $logDir ("bot_{0:D5}.log" -f $index)
    $args    = @(
        "-batchmode",
        "-nographics",
        "-logFile", $logPath,
        "--bot",
        "--bot-index=$index",
        "--bot-server=$Server",
        "--bot-secret=$Secret"
    )
    $proc = Start-Process -FilePath $resolvedBuild -ArgumentList $args -PassThru -WindowStyle Hidden
    $pids += $proc.Id
    Write-Host ("  bot_{0:D5}  pid={1}  log={2}" -f $index, $proc.Id, $logPath)
    if ($StaggerMs -gt 0 -and $i -lt $Count - 1) {
        Start-Sleep -Milliseconds $StaggerMs
    }
}

$pids | Set-Content -Path $pidFile -Encoding utf8
Write-Host ""
Write-Host "Wrote $($pids.Count) PIDs to $pidFile"
Write-Host "Stop with: ./stop-bots.ps1"
