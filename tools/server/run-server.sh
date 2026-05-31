#!/usr/bin/env bash
# Wrapper to launch the Unity dedicated server with the right environment.
# Adjust the paths below for your VPS layout. Designed to be invoked by
# systemd (see simple-town-server.service) or manually for ad-hoc runs.

set -euo pipefail

SERVER_DIR="${SERVER_DIR:-/opt/simple-town/server}"
BINARY="${BINARY:-${SERVER_DIR}/Simple Town.x86_64}"
LOG_DIR="${LOG_DIR:-${SERVER_DIR}/Logs}"

mkdir -p "${LOG_DIR}"

# ── Logging ─────────────────────────────────────────────────────────────────
# Seq runs on the same host via docker-compose. The container exposes :5341
# on the simpletown bridge network; from the host process it's reachable on
# localhost:5341.
export SEQ_ENABLED="${SEQ_ENABLED:-true}"
export SEQ_URL="${SEQ_URL:-http://localhost:5341}"
export LOG_LEVEL="${LOG_LEVEL:-Information}"
export ENVIRONMENT="${ENVIRONMENT:-Production}"

# ── Backend ─────────────────────────────────────────────────────────────────
# NestJS runs on the same VPS, loopback only — sub-ms latency to ApiManager.
export API_URL="${API_URL:-http://localhost:3000}"

# ── Monitoring ──────────────────────────────────────────────────────────────
export HEALTH_PORT="${HEALTH_PORT:-8080}"

# ── Run ─────────────────────────────────────────────────────────────────────
# -batchmode + -nographics  : headless, no rendering
# -logFile                  : Unity's native log goes to a file, Serilog handles
#                             structured logs in parallel via SEQ_URL + ./Logs/
cd "${SERVER_DIR}"
exec "${BINARY}" \
    -batchmode \
    -nographics \
    -logFile "${LOG_DIR}/unity-native.log"
