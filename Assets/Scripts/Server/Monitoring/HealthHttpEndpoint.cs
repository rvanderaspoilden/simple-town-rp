using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mirror;
using Sim.Logging;
using UnityEngine;

namespace Sim.Server.Monitoring {
    /// <summary>
    /// Tiny HTTP endpoint for liveness probes and ad-hoc debug — sits next to
    /// the Mirror server, runs on a separate TCP port (HEALTH_PORT, default 8080)
    /// so it never collides with the game traffic.
    ///
    ///   GET /health   → 200 {"status":"ok","uptime":...,"connections":...}
    ///   GET /metrics  → 200 JSON snapshot of MetricsReporter.LatestSnapshot
    ///
    /// Used by: VPS systemd/healthcheck, curl from the dev machine over an SSH
    /// tunnel, and any future external uptime monitor. Does NOT replace Seq —
    /// Seq is where you go for history + filtering, this is the "is it still
    /// alive right now" probe.
    /// </summary>
    public class HealthHttpEndpoint : MonoBehaviour {
        public static HealthHttpEndpoint Instance { get; private set; }

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private float _bootTime;

        // Snapshot of main-thread Unity state, refreshed once per frame in Update().
        // HandleRequest runs on a thread-pool worker, so it cannot read NetworkServer.*
        // or Time.* directly — those APIs throw UnityException off the main thread.
        // We read the snapshot from worker threads via volatile (struct-style atomic
        // read of references) without locks; staleness is at most one frame, which
        // is fine for a /health probe.
        private volatile MainThreadSnapshot _mainSnapshot = new MainThreadSnapshot {
            ServerActive = false,
            Connections  = 0,
            UnityTime    = 0f,
        };

        private sealed class MainThreadSnapshot {
            public bool   ServerActive;
            public int    Connections;
            public float  UnityTime;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap() {
            // Server-only — same guard as MetricsReporter / ServerLoggerInitializer.
            if (!Application.isBatchMode && !NetworkServer.active && !Application.isEditor) return;
            if (Instance != null) return;

            var go = new GameObject("[HealthHttpEndpoint]");
            Instance = go.AddComponent<HealthHttpEndpoint>();
            DontDestroyOnLoad(go);
        }

        private void Awake() {
            _bootTime = Time.unscaledTime;
            int port = ResolvePort();
            try {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://+:{port}/");
                _listener.Start();
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => AcceptLoop(_cts.Token));
                GameLogger.System.Info("HealthHttpEndpoint listening on port {Port}", port);
            } catch (Exception ex) {
                // Port already in use, no permission to bind (`+` may require admin),
                // etc. — log and bail, the server should keep running without us.
                GameLogger.System.Warning("HealthHttpEndpoint failed to start on port {Port}: {Error}", port, ex.Message);
                _listener = null;
            }
        }

        private void Update() {
            // Refresh the snapshot every frame on the main thread. HandleRequest
            // running on a worker thread reads it without going through Unity APIs.
            _mainSnapshot = new MainThreadSnapshot {
                ServerActive = NetworkServer.active,
                Connections  = NetworkServer.active ? NetworkServer.connections.Count : 0,
                UnityTime    = Time.unscaledTime,
            };
        }

        private void OnDestroy() {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { /* shutting down */ }
            try { _listener?.Close(); } catch { /* shutting down */ }
            _listener = null;
        }

        private static int ResolvePort() {
            string raw = Environment.GetEnvironmentVariable("HEALTH_PORT");
            return int.TryParse(raw, out int p) && p > 0 ? p : 8080;
        }

        private async Task AcceptLoop(CancellationToken token) {
            while (!token.IsCancellationRequested && _listener != null && _listener.IsListening) {
                HttpListenerContext ctx;
                try {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                } catch (ObjectDisposedException) {
                    return;
                } catch (HttpListenerException) {
                    // listener stopped, exit gracefully
                    return;
                } catch (Exception ex) {
                    GameLogger.System.Warning("HealthHttpEndpoint accept error: {Error}", ex.Message);
                    continue;
                }
                // Fire-and-forget the response; we don't want one slow client to
                // block the accept loop.
                _ = Task.Run(() => HandleRequest(ctx));
            }
        }

        private void HandleRequest(HttpListenerContext ctx) {
            try {
                string path = ctx.Request.Url?.AbsolutePath ?? "/";
                string json;
                int status;
                switch (path) {
                    case "/health":
                        (json, status) = BuildHealthJson();
                        break;
                    case "/metrics":
                        (json, status) = BuildMetricsJson();
                        break;
                    default:
                        json = "{\"error\":\"not found\"}";
                        status = 404;
                        break;
                }
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            } catch (Exception ex) {
                GameLogger.System.Warning("HealthHttpEndpoint write error: {Error}", ex.Message);
            } finally {
                try { ctx.Response.OutputStream.Close(); } catch { /* client gone */ }
            }
        }

        private (string json, int status) BuildHealthJson() {
            // Read from the main-thread snapshot — never call NetworkServer.* or
            // Time.* directly here (this method runs on a thread-pool worker via
            // Task.Run in HandleRequest, and those APIs throw off the main thread).
            var snap = _mainSnapshot;
            float uptime = snap.UnityTime - _bootTime;
            string status = snap.ServerActive ? "ok" : "starting";
            string json = "{" +
                $"\"status\":\"{status}\"," +
                $"\"uptimeSec\":{uptime.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"connections\":{snap.Connections}" +
                "}";
            return (json, snap.ServerActive ? 200 : 503);
        }

        private (string json, int status) BuildMetricsJson() {
            var snap = MetricsReporter.Instance?.LatestSnapshot;
            if (snap == null) {
                return ("{\"status\":\"no-snapshot-yet\"}", 503);
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string json = "{" +
                $"\"takenAtUtc\":\"{snap.TakenAtUtc:O}\"," +
                $"\"connections\":{snap.Connections}," +
                $"\"avgFrameMs\":{snap.AvgFrameMs.ToString("F2", inv)}," +
                $"\"monoMB\":{snap.MonoMB}," +
                $"\"totalMB\":{snap.TotalMB}," +
                $"\"gfxMB\":{snap.GfxMB}," +
                $"\"gcGen0Delta\":{snap.GcGen0Delta}," +
                $"\"uptimeSec\":{snap.UptimeSec.ToString("F1", inv)}" +
                "}";
            return (json, 200);
        }
    }
}
