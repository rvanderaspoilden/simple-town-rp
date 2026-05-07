using System;
using UnityEngine;

namespace Sim.Logging {
    [Serializable]
    public class GameLoggerSettings {
        [Header("Log Levels")]
        [Tooltip("Minimum log level (Debug, Information, Warning, Error, Fatal)")]
        public LogLevel minimumLevel = LogLevel.Information;

        [Header("File Output")]
        [Tooltip("Enable file logging")]
        public bool enableFileLogging = true;

        [Tooltip("Custom log directory (leave empty for default)")]
        public string customLogDirectory = "";

        [Tooltip("Number of days to retain log files (0 = unlimited)")]
        [Range(0, 365)]
        public int retainedFileCountLimit = 30;

        [Header("Seq Integration")]
        [Tooltip("Enable Seq logging")]
        public bool enableSeq = false;

        [Tooltip("Seq server URL")]
        public string seqUrl = "http://localhost:5341";

        [Tooltip("Seq API Key (optional)")]
        public string seqApiKey = "";

        [Header("Performance")]
        [Tooltip("Buffer logs before writing to disk")]
        public bool buffered = true;

        [Tooltip("Flush interval in seconds")]
        [Range(1, 60)]
        public int flushIntervalSeconds = 5;

        [Header("Enrichment")]
        [Tooltip("Add thread ID to logs")]
        public bool enrichWithThreadId = true;

        [Tooltip("Add machine name to logs")]
        public bool enrichWithMachineName = true;

        [Header("Environment")]
        [Tooltip("Environment name (Development, Staging, Production)")]
        public string environmentName = "";

        [Tooltip("Application name")]
        public string applicationName = "SimpleTownRP";
    }

    public enum LogLevel {
        Debug = 0,
        Information = 1,
        Warning = 2,
        Error = 3,
        Fatal = 4
    }

    public static class LogLevelExtensions {
        public static Serilog.Events.LogEventLevel ToSerilogLevel(this LogLevel level) {
            switch (level) {
                case LogLevel.Debug: return Serilog.Events.LogEventLevel.Debug;
                case LogLevel.Information: return Serilog.Events.LogEventLevel.Information;
                case LogLevel.Warning: return Serilog.Events.LogEventLevel.Warning;
                case LogLevel.Error: return Serilog.Events.LogEventLevel.Error;
                case LogLevel.Fatal: return Serilog.Events.LogEventLevel.Fatal;
                default: return Serilog.Events.LogEventLevel.Information;
            }
        }
    }
}
