using System;
using System.IO;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using UnityEngine;
#if SERILOG_AVAILABLE
using Serilog.Formatting.Compact;
#endif

namespace Sim.Logging {
    public static class LoggerBootstrap {
        private static bool _isInitialized = false;

        public static void Initialize(GameLoggerSettings settings = null) {
            if (_isInitialized) {
                Debug.LogWarning("LoggerBootstrap already initialized, skipping");
                return;
            }

            try {
                var logDirectory = GetLogDirectory();
                EnsureDirectoryExists(logDirectory);
                
                var minimumLevel = GetMinimumLogLevel();
                var settingsInstance = settings ?? new GameLoggerSettings();

                var configuration = new LoggerConfiguration()
                    .MinimumLevel.Is(minimumLevel)
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .Enrich.WithProperty("Application", settingsInstance.applicationName)
                    .Enrich.WithProperty("Environment", 
                        string.IsNullOrEmpty(settingsInstance.environmentName) ? GetEnvironmentName() : settingsInstance.environmentName)
                    .Enrich.WithProperty("MachineName", Environment.MachineName);

                try {
                    configuration = configuration.Enrich.WithThreadId();
                } catch {
                    Debug.LogWarning("Serilog.Enrichers.Thread not available");
                }

                try {
                    configuration = configuration.WriteTo.File(
                        new CompactJsonFormatter(),
                        Path.Combine(logDirectory, "log-.json"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: null,
                        buffered: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(5)
                    );

                } catch {
                    configuration = configuration.WriteTo.File(
                        Path.Combine(logDirectory, "log-.txt"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30
                    );
                }
                
                if (IsSeqEnabled()) {
                    var seqUrl = GetSeqUrl();
                    try {
                        configuration = configuration.WriteTo.Seq(seqUrl);
                        Debug.Log("Seq logging enabled at " + seqUrl);
                    } catch (Exception seqEx) {
                        Debug.LogWarning("Failed to configure Seq: " + seqEx.Message);
                    }
                }

                if (Application.isEditor) {
                    configuration = configuration.WriteTo.Sink(new UnityEditorSink());
                }

                Log.Logger = configuration.CreateLogger();

                _isInitialized = true;

                AttachUnityLogForwarder();

                Log.Information("Logger initialized successfully at {LogDirectory} with level {MinimumLevel}",
                    logDirectory, minimumLevel);
            }
            catch (Exception ex) {
                Debug.LogError("Failed to initialize Serilog: " + ex.Message);
                Debug.LogException(ex);
                CreateFallbackLogger();
            }
        }

        public static void Shutdown() {
            if (!_isInitialized) return;

            try {
                DetachUnityLogForwarder();
                Log.Information("Shutting down logger");
                Log.CloseAndFlush();
                _isInitialized = false;
            }
            catch (Exception ex) {
                Debug.LogError("Error during logger shutdown: " + ex.Message);
            }
        }

        // ─── Unity → Serilog forwarder ──────────────────────────────────────
        // Captures Debug.LogError / Debug.LogException / Debug.LogWarning that
        // originate outside GameLogger (Mirror, Dissonance, Unity core), so they
        // converge in the same Seq + File sinks. Without this, an uncaught Mirror
        // exception never leaves the server's local file.
        //
        // Loop protection: UnityEditorSink calls Debug.LogError when it emits an
        // Error-level event in the editor, which would re-enter this forwarder.
        // The ThreadStatic flag below blocks the recursion (same-thread emit).

        [ThreadStatic] private static bool _forwardingInProgress;
        private static bool _forwarderAttached;
        private static Application.LogCallback _forwarderDelegate;

        private static void AttachUnityLogForwarder() {
            if (_forwarderAttached) return;
            _forwarderDelegate = OnUnityLog;
            Application.logMessageReceivedThreaded += _forwarderDelegate;
            _forwarderAttached = true;
        }

        private static void DetachUnityLogForwarder() {
            if (!_forwarderAttached) return;
            Application.logMessageReceivedThreaded -= _forwarderDelegate;
            _forwarderAttached = false;
        }

        private static void OnUnityLog(string message, string stackTrace, LogType type) {
            if (_forwardingInProgress) return;
            _forwardingInProgress = true;
            try {
                switch (type) {
                    case LogType.Exception:
                    case LogType.Error:
                        Log.Error("{UnitySource} {Message}\n{Stack}", "Unity", message, stackTrace);
                        break;
                    case LogType.Warning:
                        Log.Warning("{UnitySource} {Message}", "Unity", message);
                        break;
                    // LogType.Log / Assert are skipped intentionally — too noisy and
                    // anything we care about already goes through GameLogger.
                }
            } catch {
                // Never let the forwarder throw — that would brick the Unity log pipeline.
            } finally {
                _forwardingInProgress = false;
            }
        }

        private static string GetLogDirectory() {
            var baseDirectory = Path.Combine(Application.dataPath, "..", "Logs");

            return Path.GetFullPath(baseDirectory);
        }

        private static void EnsureDirectoryExists(string path) {
            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }
        }

        private static LogEventLevel GetMinimumLogLevel() {
            var levelString = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information";
            
            if (Enum.TryParse<LogEventLevel>(levelString, true, out var level)) {
                return level;
            }

            return Application.isEditor ? LogEventLevel.Debug : LogEventLevel.Information;
        }

        private static string GetEnvironmentName() {
            return Environment.GetEnvironmentVariable("ENVIRONMENT") 
                ?? (Application.isEditor ? "Development" : "Production");
        }

        private static bool IsSeqEnabled() {
            var enabled = Environment.GetEnvironmentVariable("SEQ_ENABLED");
            return !string.IsNullOrEmpty(enabled) && enabled.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSeqUrl() {
            return Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341";
        }

        private static void CreateFallbackLogger() {
            try {
                var logDirectory = GetLogDirectory();
                EnsureDirectoryExists(logDirectory);
                
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        Path.Combine(logDirectory, "fallback-.txt"),
                        rollingInterval: RollingInterval.Day
                    )
                    .CreateLogger();
                
                _isInitialized = true;
                Debug.LogWarning("Using fallback file logger due to initialization error");
            }
            catch {
                // Ultimate fallback - just use Unity's Debug.Log
                Debug.LogError("CRITICAL: Could not initialize any logger. Using Unity Debug.Log only.");
            }
        }
    }
}
