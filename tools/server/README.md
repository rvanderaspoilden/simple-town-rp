# Remote server deployment + monitoring

Stack for running the Unity Mirror server on a Linux VPS with structured logs
streamed to a self-hosted Seq dashboard, plus a tiny `/health` endpoint for
liveness probes.

## What's in this folder

| File | Purpose |
|---|---|
| `docker-compose.yml` | Seq + Caddy reverse proxy (run on the VPS) |
| `Caddyfile` | TLS + basic auth for the Seq web UI |
| `.env.example` | Template for the secrets `docker-compose` reads |
| `run-server.sh` | Wrapper that launches `Simple Town.x86_64` headless |
| `simple-town-server.service` | systemd unit so the server auto-restarts |

## Architecture

```
VPS Linux
├── Unity server (Simple Town.x86_64 -batchmode -nographics)
│    ├── Serilog → file sink (./Logs/log-*.json, rolling daily)
│    ├── Serilog → Seq sink   (http://localhost:5341, native HTTP)
│    └── HealthHttpEndpoint   (http://localhost:8080/health, /metrics)
│
└── docker-compose
     ├── seq      (port 5341 ingest, port 80 internal UI)
     └── caddy    (ports 80/443 external, terminates TLS + basic auth)
```

The Unity server writes events to two sinks: a local file (cheap, always-on
fallback) and Seq (the web dashboard you actually look at). The `/health`
endpoint is separate — it's there for `curl`-from-anywhere and uptime monitors,
not as the primary observability surface.

## Prerequisites on the VPS

1. Docker + docker compose plugin installed
2. Ports 80, 443 open on the firewall (Caddy)
3. Port 7777 open (Mirror server's TCP listener, or whatever
   `SimpleTownNetwork.networkAddress` is configured for)
4. (Optional) A DNS A record pointing to the VPS IP, used by Caddy for
   automatic Let's Encrypt certs

## First-time setup

```bash
# On the VPS
mkdir -p /opt/simple-town/server
cd /opt/simple-town/server

# Drop this whole tools/server/ folder here
scp -r tools/server/* vps:/opt/simple-town/server/

# Create the simpletown system user
sudo useradd --system --no-create-home simpletown
sudo chown -R simpletown:simpletown /opt/simple-town

# Generate Seq + Caddy hashes interactively, paste into .env
docker run --rm -it datalust/seq config hash     # → SEQ_ADMIN_PASSWORD_HASH
docker run --rm -it caddy caddy hash-password    # → SEQ_BASIC_AUTH_HASH

cp .env.example .env && nano .env                # paste hashes + set SEQ_HOST

# Bring up the dashboard stack
docker compose --env-file .env up -d

# Upload the Unity server build (from your dev machine)
scp -r Builds/Server/* vps:/opt/simple-town/server/
chmod +x /opt/simple-town/server/run-server.sh

# Install the systemd unit
sudo cp simple-town-server.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now simple-town-server
```

## Day-to-day

```bash
# Tail the live Unity native log
sudo journalctl -u simple-town-server -f
# or
tail -f /opt/simple-town/server/Logs/unity-native.log

# Check liveness without opening Seq
curl http://localhost:8080/health
curl http://localhost:8080/metrics

# Restart the server (zero downtime not yet a thing)
sudo systemctl restart simple-town-server

# Stop the Seq stack
docker compose down
```

## Accessing Seq

- With a domain: `https://seq.your-domain.tld` → basic auth → log in with the
  Seq admin password you hashed earlier
- Without a domain (or behind NAT): SSH tunnel
  ```bash
  ssh -L 8080:localhost:80 vps
  # then open http://localhost:8080 in your browser
  ```

### Useful saved queries

In Seq, save these as Signals for one-click filtering:

| Signal | Query |
|---|---|
| Server metrics | `@MessageTemplate like '%ServerMetrics%'` |
| Network errors | `Category = 'Network' and @Level in ['Error','Fatal']` |
| Unity exceptions | `UnitySource = 'Unity' and @Level = 'Error'` |
| Bots connecting | `@MessageTemplate like '%CreateCharacterRequest%' and SpawnInCity = true` |

The `ServerMetrics` event carries structured props (`Connections`, `AvgFrameMs`,
`MonoMB`, `TotalMB`, `GfxMB`, `GcGen0Delta`, `UptimeSec`) → you can chart them
directly in Seq's dashboard view, no extra config.

### Import the "Simple Town Server" dashboard

A pre-built dashboard with four perf charts (active connections, avg frame
time vs 16ms target, memory breakdown, GC Gen0 rate) lives in
`tools/server/seq-dashboard.json`. Import it once:

1. Open Seq UI → top-right user menu → **Settings**
2. Left sidebar → **Data ▸ Import**
3. Drag-drop `seq-dashboard.json` (or paste the file contents)
4. Confirm → the dashboard appears under **Dashboards** in the main menu

Once imported you can rename it, change time ranges, or tweak the queries
inline in the UI. Re-export and overwrite `seq-dashboard.json` to keep the
git copy in sync.

**If the import fails** (Seq schemas drift between releases), build it
manually — it takes ~3 minutes:

1. Dashboards → **+ New dashboard**, name it "Simple Town Server"
2. **+ Add chart**, paste the SQL below, pick a chart type, save. Repeat 4×.
3. **Export** the result (chart context menu) and overwrite the JSON.

| Chart | Type | SQL |
|---|---|---|
| Active connections | Line | `select mean(Connections) as Connections from stream where @MessageTemplate like '%ServerMetrics%' group by time(30s)` |
| Avg frame time (target 16ms) | Line | `select mean(AvgFrameMs) as AvgMs, 16 as Target60Hz from stream where @MessageTemplate like '%ServerMetrics%' group by time(30s)` |
| Memory MB (Mono / Total / Gfx) | Line | `select mean(MonoMB) as Mono, mean(TotalMB) as Total, mean(GfxMB) as Gfx from stream where @MessageTemplate like '%ServerMetrics%' group by time(30s)` |
| GC Gen0 per minute | Bar | `select sum(GcGen0Delta) as Gen0PerMin from stream where @MessageTemplate like '%ServerMetrics%' group by time(1m)` |

**How to read it during a stress test**:
- **Connections** drops in steps → bots crashing or server kicking them
- **AvgMs** crosses 16ms line → server tick budget blown, sync will degrade
- **Memory** climbs without plateauing → leak (Mono) or asset growth (Gfx)
- **GC Gen0** spikes per minute → too many allocs/frame in hot path

## Environment variables consumed by the Unity server

| Variable | Default | Effect |
|---|---|---|
| `SEQ_ENABLED` | unset (off) | `true` activates the Seq sink |
| `SEQ_URL` | `http://localhost:5341` | Seq ingest URL |
| `LOG_LEVEL` | `Information` | Serilog minimum level |
| `ENVIRONMENT` | `Production` (non-editor) | Enriches every event |
| `HEALTH_PORT` | `8080` | Port for `/health` and `/metrics` |

## First-time setup — Backend (NestJS)

The backend runs side-by-side with the Mirror server on the same VPS, listening
on `:3000` (loopback only — exposed publicly via the same `7777` reachability
required for Mirror is **not** what we want here; only the bot stress-test
endpoint needs the public port, see "Network exposure" below).

```bash
# As root on the VPS
mkdir -p /opt/simple-town/backend/logs
chown -R simpletown:simpletown /opt/simple-town/backend

# Install Node 20 LTS from NodeSource
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt install -y nodejs

# Copy the .env (contains SUPABASE_URL, SUPABASE_SERVICE_KEY, JWT_SECRET,
# BOT_REGISTRATION_SECRET, BOT_DEFAULT_PRESET, NODE_ENV=production)
scp .env.production vps:/opt/simple-town/backend/.env

# Install the systemd unit (from this folder)
sudo cp simple-town-backend.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable simple-town-backend
# Do NOT start it yet — wait for the first CI deploy to push dist/.

# Grant simpletown a narrow sudoers permission to restart the service
sudo visudo -f /etc/sudoers.d/simpletown
# Append:
#   simpletown ALL=NOPASSWD: /bin/systemctl restart simple-town-backend
#   simpletown ALL=NOPASSWD: /bin/systemctl restart simple-town-server
```

The first GitHub Actions deploy (see backend repo) will push `dist/` +
`package.json` to `/opt/simple-town/backend/`, run `npm ci --omit=dev`, then
restart the service.

## Deploying the Mirror server (manual)

Mirror restarts kick every connected player, so deploys are explicit and
local — no GitHub Actions for Unity in V1.

1. In the Unity Editor, switch to the **Server Linux** build profile and Build
   (output: `Builds/Server/Simple Town.x86_64`)
2. From the repo root:
   ```powershell
   $env:VPS_HOST = "vps.example.tld"
   ./tools/server/deploy-mirror.ps1
   ```
3. The script rsyncs the build, restarts the systemd service, then polls
   `http://vps:8080/health` until it returns 200. It will exit non-zero if
   the health check fails — check `journalctl -u simple-town-server -n 100`
   on the VPS to investigate.

`deploy-mirror.ps1` requires `ssh` on PATH (Windows 10+ ships OpenSSH). It
uses `rsync` when available (WSL, MSYS2, Git Bash) and falls back to `scp -r`
otherwise.

## Network exposure (firewall / UFW)

| Port | Service | Visibility |
|---|---|---|
| 22 | SSH | Public, key-only |
| 80/443 | Caddy → Seq | Public (basicauth) |
| 3000 | NestJS backend | **Public** — bots in stress test call `/auth/register-bot` from outside |
| 7777 | Mirror | Public — game clients connect here |
| 5341 | Seq ingest | Private (Unity server uses localhost, no need to expose) |
| 8080 | HealthHttpEndpoint | Private (curl via SSH tunnel for ad-hoc checks) |

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 3000/tcp
sudo ufw allow 7777/tcp
sudo ufw enable
```

## Building the Linux server

In the Unity Editor:
1. Duplicate the existing **Bot Headless** Build Profile (Window → Build
   Profiles, right click → Duplicate). Rename it to **Server Linux**.
2. Edit the new profile:
   - Platform: **Linux x86_64**, Scripting Backend: **IL2CPP**
   - **Server Build: ✓** (this is the key flag — strips client-only code)
   - Development Build: ✗
   - Scripting Defines: **remove** `STRESS_TEST_BOTS` if present (the server
     must never include bot orchestration code)
   - Scenes: same as the default Windows profile (Launcher, Main Menu, City,
     SubGames/*)
3. Build → output `Builds/Server/Simple Town.x86_64` + `Simple Town_Data/`
4. `scp -r Builds/Server/* vps:/opt/simple-town/server/`

## Troubleshooting

**Seq UI shows no events**
- Check `SEQ_ENABLED=true` is exported in `run-server.sh` (or in the systemd
  EnvironmentFile)
- Confirm the Seq container is reachable: `curl http://localhost:5341` from
  the VPS shell should return Seq's HTML

**`/health` returns 503 with `status: starting`**
- The Unity server is up but `NetworkServer.active` is still false. This is
  normal during boot — wait a couple of seconds and retry. If it never flips
  to 200, the Mirror startup is failing (check Seq for `ServerStarting`
  followed by an error)

**`HealthHttpEndpoint failed to start on port 8080`**
- Another process is bound to 8080. Either kill it or set
  `HEALTH_PORT=8081` in `run-server.sh`. Binding to `http://+:8080/` may
  also need a Linux capability — usually fine for ports ≥ 1024
