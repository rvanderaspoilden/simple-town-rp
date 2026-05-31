using System;
using Mirror;
using Sim.Logging;
using UnityEngine;
using UnityEngine.Profiling;

namespace Sim.Server.Monitoring {
    /// <summary>
    /// Periodically emits a structured "ServerMetrics" Serilog event with the
    /// stats we care about for remote monitoring:
    ///   - active Mirror connections
    ///   - average frame time over the window (proxy for tick budget)
    ///   - Mono / total / graphics memory in MB
    ///   - GC gen-0 collection count delta
    ///
    /// The event flows through GameLogger.System → the same Seq + File sinks
    /// the rest of the server uses, so no new transport to maintain. Seq can
    /// graph "AvgFrameMs by Connections" directly from the structured props.
    ///
    /// Snapshot also exposed as <see cref="LatestSnapshot"/> so HealthHttpEndpoint
    /// can serve it on /metrics without re-reading the profiler.
    /// </summary>
    public class MetricsReporter : MonoBehaviour {
        public static MetricsReporter Instance { get; private set; }

        [SerializeField] private float intervalSeconds = 5f;

        public Snapshot LatestSnapshot { get; private set; }

        private float _windowStart;
        private int _frameAtWindowStart;
        private int _gcGen0AtWindowStart;
        private float _bootTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap() {
            // Server-only — we don't want every connected client polling the profiler
            // and spamming logs. Mirrors ServerLoggerInitializer's guard.
            if (!Application.isBatchMode && !NetworkServer.active && !Application.isEditor) return;
            if (Instance != null) return;

            var go = new GameObject("[MetricsReporter]");
            Instance = go.AddComponent<MetricsReporter>();
            DontDestroyOnLoad(go);
        }

        private void Awake() {
            _windowStart = Time.unscaledTime;
            _frameAtWindowStart = Time.frameCount;
            _gcGen0AtWindowStart = GC.CollectionCount(0);
            _bootTime = Time.unscaledTime;
        }

        private void Update() {
            // Only emit once a server is actually running. In a host build this
            // means NetworkServer.active; in a Server Build batchmode it's set
            // as soon as StartServer/StartHost runs.
            if (!NetworkServer.active) return;
            if (Time.unscaledTime - _windowStart < intervalSeconds) return;

            float elapsed = Time.unscaledTime - _windowStart;
            int frames = Time.frameCount - _frameAtWindowStart;
            int gcGen0 = GC.CollectionCount(0) - _gcGen0AtWindowStart;

            long monoMB = Profiler.GetMonoUsedSizeLong() >> 20;
            long totalMB = Profiler.GetTotalAllocatedMemoryLong() >> 20;
            long gfxMB = Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20;

            float avgFrameMs = frames > 0 ? (elapsed * 1000f) / frames : 0f;
            int connections = NetworkServer.connections.Count;
            float uptimeSec = Time.unscaledTime - _bootTime;

            var snap = new Snapshot {
                TakenAtUtc = DateTime.UtcNow,
                Connections = connections,
                AvgFrameMs = avgFrameMs,
                MonoMB = monoMB,
                TotalMB = totalMB,
                GfxMB = gfxMB,
                GcGen0Delta = gcGen0,
                UptimeSec = uptimeSec,
            };
            LatestSnapshot = snap;

            GameLogger.System.Info(
                "ServerMetrics {Connections} {AvgFrameMs} {MonoMB} {TotalMB} {GfxMB} {GcGen0Delta} {UptimeSec}",
                connections, avgFrameMs, monoMB, totalMB, gfxMB, gcGen0, uptimeSec);

            _windowStart = Time.unscaledTime;
            _frameAtWindowStart = Time.frameCount;
            _gcGen0AtWindowStart = GC.CollectionCount(0);
        }

        public class Snapshot {
            public DateTime TakenAtUtc;
            public int Connections;
            public float AvgFrameMs;
            public long MonoMB;
            public long TotalMB;
            public long GfxMB;
            public int GcGen0Delta;
            public float UptimeSec;
        }
    }
}
