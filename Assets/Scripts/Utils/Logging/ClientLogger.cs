using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sim.Logging {
    public static class ClientLogger {
        private static bool _isInitialized = false;
        private static LogLevel _minimumLevel = LogLevel.Information;
        private static readonly Dictionary<LogCategory, Color> CategoryColors = new Dictionary<LogCategory, Color> {
            { LogCategory.Network, new Color(0.3f, 0.7f, 1f) },      // Bleu clair
            { LogCategory.Props, new Color(0.4f, 0.9f, 0.4f) },      // Vert
            { LogCategory.Rooms, new Color(1f, 0.6f, 0.2f) },         // Orange
            { LogCategory.Player, new Color(0.9f, 0.4f, 0.9f) },       // Violet
            { LogCategory.System, new Color(0.8f, 0.8f, 0.8f) },       // Gris clair
            { LogCategory.UI, new Color(1f, 0.8f, 0.2f) },             // Jaune
            { LogCategory.Audio, new Color(0.2f, 0.8f, 0.8f) },       // Cyan
            { LogCategory.Input, new Color(0.7f, 0.7f, 1f) },          // Bleu pâle
            { LogCategory.Error, new Color(1f, 0.2f, 0.2f) }           // Rouge
        };

        public enum LogLevel { Debug, Information, Warning, Error, Fatal }
        public enum LogCategory { Network, Props, Rooms, Player, System, UI, Audio, Input, Error }

        public static void Initialize(LogLevel minimumLevel = LogLevel.Information) {
            _minimumLevel = minimumLevel;
            _isInitialized = true;
            Log(LogCategory.System, LogLevel.Information, "ClientLogger initialized", null);
        }

        public static void Shutdown() {
            _isInitialized = false;
        }

        public static bool IsEnabled(LogLevel level) {
            return _isInitialized && level >= _minimumLevel;
        }

        public static void Network(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Network, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void NetworkDebug(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Network, LogLevel.Debug, messageTemplate, propertyValues);
        }

        public static void NetworkWarning(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Network, LogLevel.Warning, messageTemplate, propertyValues);
        }

        public static void NetworkError(Exception ex, string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Network, LogLevel.Error, messageTemplate, propertyValues, ex);
        }

        public static void Props(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Props, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void PropsDebug(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Props, LogLevel.Debug, messageTemplate, propertyValues);
        }

        public static void Rooms(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Rooms, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void Player(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Player, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void System(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.System, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void UI(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.UI, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void Audio(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Audio, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void Input(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Input, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void Debug(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.System, LogLevel.Debug, messageTemplate, propertyValues);
        }

        public static void Info(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.System, LogLevel.Information, messageTemplate, propertyValues);
        }

        public static void Warning(string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.System, LogLevel.Warning, messageTemplate, propertyValues);
        }

        public static void Error(Exception ex, string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Error, LogLevel.Error, messageTemplate, propertyValues, ex);
        }

        public static void Fatal(Exception ex, string messageTemplate, params object[] propertyValues) {
            Log(LogCategory.Error, LogLevel.Fatal, messageTemplate, propertyValues, ex);
        }

        private static void Log(LogCategory category, LogLevel level, string messageTemplate, object[] propertyValues, Exception exception = null) {
            if (!IsEnabled(level)) return;

            var formattedMessage = FormatMessage(category, level, messageTemplate, propertyValues);
            var color = GetColorForCategory(category, level);
            var coloredMessage = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{formattedMessage}</color>";

            switch (level) {
                case LogLevel.Debug:
                case LogLevel.Information:
                    UnityEngine.Debug.Log(coloredMessage);
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(coloredMessage);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    if (exception != null) {
                        UnityEngine.Debug.LogError(coloredMessage + "\nException: " + exception);
                    } else {
                        UnityEngine.Debug.LogError(coloredMessage);
                    }
                    break;
            }
        }

        private static string FormatMessage(LogCategory category, LogLevel level, string messageTemplate, object[] propertyValues) {
            var sb = new StringBuilder();
            
            // Timestamp
            sb.Append("[");
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
            sb.Append("] ");
            
            // Category avec padding pour alignement
            var categoryStr = category.ToString().ToUpper();
            sb.Append(categoryStr.PadRight(8));
            sb.Append(" | ");
            
            // Level avec padding
            var levelStr = level.ToString().ToUpper();
            sb.Append(levelStr.PadRight(8));
            sb.Append(" | ");
            
            // Message
            if (propertyValues != null && propertyValues.Length > 0) {
                try {
                    var message = string.Format(messageTemplate, propertyValues);
                    sb.Append(message);
                } catch {
                    sb.Append(messageTemplate);
                    sb.Append(" | Args: ");
                    sb.Append(string.Join(", ", propertyValues));
                }
            } else {
                sb.Append(messageTemplate);
            }
            
            return sb.ToString();
        }

        private static Color GetColorForCategory(LogCategory category, LogLevel level) {
            if (level >= LogLevel.Error) {
                return CategoryColors[LogCategory.Error];
            }
            if (level == LogLevel.Warning) {
                return new Color(1f, 0.8f, 0.2f); // Jaune/orange pour warnings
            }
            return CategoryColors.TryGetValue(category, out var color) ? color : Color.white;
        }

        public static void SetMinimumLevel(LogLevel level) {
            _minimumLevel = level;
        }
    }
}
