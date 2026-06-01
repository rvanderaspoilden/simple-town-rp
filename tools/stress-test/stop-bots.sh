#!/usr/bin/env bash
# Kill every bot process spawned by launch-bots.sh.
# Reads PIDs from bots.pid and terminates each one. Missing / already-dead
# PIDs are silently skipped.

set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"

# Try Mac build first, then Linux. The PID file lives next to the binary.
for candidate in \
    "$PROJECT_ROOT/Builds/BotMac/bots.pid" \
    "$PROJECT_ROOT/Builds/Bot/bots.pid"; do
    if [[ -f "$candidate" ]]; then
        PID_FILE="$candidate"
        break
    fi
done

if [[ -z "${PID_FILE:-}" ]]; then
    echo "No PID file found. Nothing to do."
    exit 0
fi

killed=0
total=0
while read -r pid; do
    [[ -z "$pid" || "$pid" =~ ^[^0-9] ]] && continue
    total=$((total + 1))
    if kill "$pid" 2>/dev/null; then
        killed=$((killed + 1))
    fi
done < "$PID_FILE"

rm -f "$PID_FILE"
echo "Killed $killed / $total bot processes. PID file removed."
