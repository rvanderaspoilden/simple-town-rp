#!/usr/bin/env bash
# Launch N headless Unity bot processes against the simple-town-ws server.
# Mac/Linux equivalent of launch-bots.ps1.
#
# Each bot is a separate Unity process built in headless / batchmode and is
# identified by --bot-index. Bots auto-provision themselves on first launch
# via POST /auth/register-bot, then loop random NavMesh moves in the City scene.
#
# Usage:
#   ./launch-bots.sh                                                  # 10 bots, localhost
#   ./launch-bots.sh -c 25 -s http://212.227.180.107:3000              # 25 bots, VPS
#   ./launch-bots.sh -c 10 -i 100 -s http://...                       # 10 bots starting at index 100
#
# Env vars:
#   BOT_REGISTRATION_SECRET  — same secret as the backend .env (required)
#   VPS_HOST                 — optional, only used to set MIRROR_HOST below
#
# The .app's binary lives at Simple\ Town.app/Contents/MacOS/Simple\ Town on macOS,
# or directly as Simple\ Town.x86_64 on Linux. We detect both.

set -euo pipefail

COUNT=10
START_INDEX=0
SERVER="http://localhost:3000"
SECRET="${BOT_REGISTRATION_SECRET:-}"
STAGGER_MS=200
BUILD_DIR=""

while getopts "c:i:s:S:m:b:" opt; do
    case "$opt" in
        c) COUNT="$OPTARG" ;;
        i) START_INDEX="$OPTARG" ;;
        s) SERVER="$OPTARG" ;;
        S) SECRET="$OPTARG" ;;
        m) STAGGER_MS="$OPTARG" ;;
        b) BUILD_DIR="$OPTARG" ;;
        *)
            echo "Usage: $0 [-c count] [-i start-index] [-s server-url] [-S secret] [-m stagger-ms] [-b build-dir]"
            exit 1
            ;;
    esac
done

if [[ -z "$SECRET" ]]; then
    echo "ERROR: Missing bot secret. Pass -S <secret> or export BOT_REGISTRATION_SECRET." >&2
    exit 1
fi

# Resolve paths relative to the script — works whether you call from anywhere.
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"

# Auto-detect the binary if -b not given. macOS .app vs Linux x86_64.
if [[ -z "$BUILD_DIR" ]]; then
    if [[ -d "$PROJECT_ROOT/Builds/BotMac/Simple Town.app" ]]; then
        BUILD_DIR="$PROJECT_ROOT/Builds/BotMac"
    elif [[ -f "$PROJECT_ROOT/Builds/Bot/Simple Town.x86_64" ]]; then
        BUILD_DIR="$PROJECT_ROOT/Builds/Bot"
    else
        echo "ERROR: No bot build found. Looked at:" >&2
        echo "  $PROJECT_ROOT/Builds/BotMac/Simple Town.app" >&2
        echo "  $PROJECT_ROOT/Builds/Bot/Simple Town.x86_64" >&2
        echo "Build 'Bot Headless Mac' or 'Bot Headless' first, or pass -b <dir>." >&2
        exit 1
    fi
fi

# Pick the actual executable inside the build dir.
if [[ -d "$BUILD_DIR/Simple Town.app" ]]; then
    BINARY="$BUILD_DIR/Simple Town.app/Contents/MacOS/Simple Town"
elif [[ -f "$BUILD_DIR/Simple Town.x86_64" ]]; then
    BINARY="$BUILD_DIR/Simple Town.x86_64"
else
    echo "ERROR: No executable inside $BUILD_DIR" >&2
    exit 1
fi

LOG_DIR="$BUILD_DIR/logs"
PID_FILE="$BUILD_DIR/bots.pid"
mkdir -p "$LOG_DIR"

# Same trick as the PowerShell version: derive MIRROR_HOST from -s so the bot's
# Mirror StartClient targets the same host as the API. Without it,
# SimpleTownNetwork falls back to the bot's PlayerPrefs (= "localhost").
if [[ "$SERVER" =~ ^https?://([^:/]+) ]]; then
    export MIRROR_HOST="${BASH_REMATCH[1]}"
    echo "Setting MIRROR_HOST=$MIRROR_HOST for spawned bots"
fi

echo "Launching $COUNT bots, indices [$START_INDEX..$((START_INDEX + COUNT - 1))] -> $SERVER"
# APPEND to the PID file across successive paliers. stop-bots.sh wipes the file
# after a full teardown, so the next "fresh" launch starts empty automatically.
# Ensure the file exists so the `>> $PID_FILE` calls below have something to
# append to.
touch "$PID_FILE"

for ((i = 0; i < COUNT; i++)); do
    idx=$((START_INDEX + i))
    log_path="$LOG_DIR/$(printf 'bot_%05d.log' "$idx")"

    # nohup + & detaches so closing the terminal doesn't kill the bots.
    nohup "$BINARY" \
        -batchmode \
        -nographics \
        -logFile "$log_path" \
        --bot \
        --bot-index="$idx" \
        --bot-server="$SERVER" \
        --bot-secret="$SECRET" \
        >/dev/null 2>&1 &

    echo "$!" >> "$PID_FILE"
    printf '  bot_%05d  pid=%d  log=%s\n' "$idx" "$!" "$log_path"

    if (( STAGGER_MS > 0 && i < COUNT - 1 )); then
        sleep "$(awk "BEGIN { printf \"%.3f\", $STAGGER_MS/1000 }")"
    fi
done

echo
total=$(wc -l < "$PID_FILE" | tr -d ' ')
echo "Added $COUNT PIDs to $PID_FILE  (total tracked: $total)"
echo "Stop ALL tracked bots with: ./stop-bots.sh"
