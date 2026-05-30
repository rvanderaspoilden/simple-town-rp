<#
.SYNOPSIS
    Wipe every bot_* account + character + home + place from the backend DB.

.DESCRIPTION
    Calls POST /auth/bots/cleanup which is dev-only (NODE_ENV != production)
    and gated by BOT_REGISTRATION_SECRET. Prints the deletion breakdown.

    Use after a stress-test session to reset the DB state. Next launch-bots
    run will re-provision bot_00000.. from scratch.

.PARAMETER Server
    Backend URI. Default: http://localhost:3000

.PARAMETER Secret
    Shared secret. Read from $env:BOT_REGISTRATION_SECRET by default.
#>
param(
    [string] $Server = "http://localhost:3000",
    [string] $Secret = $env:BOT_REGISTRATION_SECRET
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($Secret)) {
    Write-Error "Missing bot secret. Pass -Secret or set BOT_REGISTRATION_SECRET."
    exit 1
}

$headers = @{ "x-bot-secret" = $Secret; "Content-Type" = "application/json" }
$response = Invoke-RestMethod -Method Post -Uri "$Server/auth/bots/cleanup" -Headers $headers -Body "{}"

Write-Host "Cleanup result:"
$response | Format-List
