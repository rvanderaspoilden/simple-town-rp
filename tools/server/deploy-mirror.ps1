<#
.SYNOPSIS
    Deploy the locally-built Mirror server to the VPS and restart it.

.DESCRIPTION
    Workflow assumed: build the "Server Linux" profile in the Unity Editor
    (output Builds/Server/Simple Town.x86_64), then run this script. It:
      1. Validates that the build exists
      2. rsyncs Builds/Server/ to the VPS
      3. Sets exec bits + restarts the systemd service
      4. Polls /health on the VPS to confirm the server is responding

    Manual on purpose: restarting Mirror kicks every connected player/bot,
    so we don't want this to fire on every git push.

.PARAMETER Server
    VPS hostname or IP. Defaults to $env:VPS_HOST.

.PARAMETER User
    SSH user on the VPS. Defaults to "simpletown".

.PARAMETER BuildPath
    Local path to the build folder, relative to the repo root.

.PARAMETER Remote
    Absolute path on the VPS where the server lives.

.PARAMETER HealthPort
    Port HealthHttpEndpoint listens on (must match HEALTH_PORT in run-server.sh).

.EXAMPLE
    ./deploy-mirror.ps1 -Server 1.2.3.4

.EXAMPLE
    $env:VPS_HOST = "vps.example.tld"; ./deploy-mirror.ps1
#>
param(
    [string] $Server     = $env:VPS_HOST,
    [string] $User       = "simpletown",
    [string] $BuildPath  = "Builds/Server",
    [string] $Remote     = "/opt/simple-town/server",
    [int]    $HealthPort = 8080
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($Server)) {
    Write-Error "Missing VPS host. Pass -Server or set VPS_HOST."
    exit 1
}

$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectRoot = (Resolve-Path (Join-Path $scriptDir "../..")).Path
$localBuild  = Join-Path $projectRoot $BuildPath

if (-not (Test-Path $localBuild)) {
    Write-Error "Build not found at $localBuild. Build the 'Server Linux' profile in Unity first."
    exit 1
}

$binary = Join-Path $localBuild "Simple Town.x86_64"
if (-not (Test-Path $binary)) {
    Write-Error "Missing binary $binary - check that the Server Linux profile output is correct."
    exit 1
}

Write-Host "Deploying $localBuild  ->  ${User}@${Server}:${Remote}"

# rsync from Windows: relies on OpenSSH being on PATH (Windows 10+) and either
# WSL/Cygwin rsync OR the new ssh-based one. Fallback to scp -r if rsync is
# not installed.
$rsyncAvailable = $null -ne (Get-Command rsync -ErrorAction SilentlyContinue)

if ($rsyncAvailable) {
    # Trailing slash on source = "copy contents of", not the folder itself.
    & rsync -avz --delete --info=progress2 "$localBuild/" "${User}@${Server}:${Remote}/"
    if ($LASTEXITCODE -ne 0) { Write-Error "rsync failed"; exit 1 }
} else {
    # Fallback: tar locally, scp the tarball, extract remotely. Works on Windows
    # native (tar.exe shipped since Win10 1803), handles spaces in filenames,
    # avoids the scp wildcard headache, and is faster than scp -r over slow links.
    Write-Warning "rsync not found - using tar+scp+ssh fallback (Windows-native, no delete on remote)."

    $tarball = Join-Path $env:TEMP "simple-town-server-deploy.tar"
    if (Test-Path $tarball) { Remove-Item $tarball -Force }

    # -C cd into the source dir, then . = pack its contents (not the dir itself)
    & tar -cf $tarball -C $localBuild .
    if ($LASTEXITCODE -ne 0) { Write-Error "local tar failed"; exit 1 }

    & scp $tarball "${User}@${Server}:/tmp/simple-town-server-deploy.tar"
    if ($LASTEXITCODE -ne 0) { Write-Error "scp of tarball failed"; exit 1 }

    & ssh "${User}@${Server}" "tar -xf /tmp/simple-town-server-deploy.tar -C '${Remote}' && rm /tmp/simple-town-server-deploy.tar"
    if ($LASTEXITCODE -ne 0) { Write-Error "remote extract failed"; exit 1 }

    Remove-Item $tarball -Force -ErrorAction SilentlyContinue
}

Write-Host "Setting exec bits + restarting service..."
$remoteCmd = @"
set -euo pipefail
chmod +x '${Remote}/Simple Town.x86_64' '${Remote}/run-server.sh' || true
sudo systemctl restart simple-town-server
"@
& ssh "${User}@${Server}" $remoteCmd
if ($LASTEXITCODE -ne 0) { Write-Error "Remote restart failed"; exit 1 }

Write-Host "Waiting for /health to respond..."
$healthUrl = "http://${Server}:${HealthPort}/health"
$ok = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Seconds 2
    try {
        $resp = Invoke-WebRequest -Uri $healthUrl -TimeoutSec 3 -UseBasicParsing
        if ($resp.StatusCode -eq 200) {
            Write-Host ""
            Write-Host "Health check OK after $($i * 2)s:"
            Write-Host $resp.Content
            $ok = $true
            break
        }
    } catch {
        Write-Host -NoNewline "."
    }
}

if (-not $ok) {
    Write-Error "Health check failed after 40s. Check 'journalctl -u simple-town-server -n 100' on the VPS."
    exit 1
}

Write-Host ""
Write-Host "Deploy complete."
