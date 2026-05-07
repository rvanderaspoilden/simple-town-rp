using System;
using Serilog;
using Serilog.Events;

namespace Sim.Logging {
    public static class GameLogger {
        public static NetworkLogger Network { get; } = new NetworkLogger();
        public static PropsLogger Props { get; } = new PropsLogger();
        public static RoomsLogger Rooms { get; } = new RoomsLogger();
        public static PlayerLogger Player { get; } = new PlayerLogger();
        public static SystemLogger System { get; } = new SystemLogger();

        public static void Debug(string messageTemplate, params object[] propertyValues) {
            Log.Debug(messageTemplate, propertyValues);
        }

        public static void Info(string messageTemplate, params object[] propertyValues) {
            Log.Information(messageTemplate, propertyValues);
        }

        public static void Warning(string messageTemplate, params object[] propertyValues) {
            Log.Warning(messageTemplate, propertyValues);
        }

        public static void Error(Exception exception, string messageTemplate, params object[] propertyValues) {
            Log.Error(exception, messageTemplate, propertyValues);
        }

        public static void Error(string messageTemplate, params object[] propertyValues) {
            Log.Error(messageTemplate, propertyValues);
        }

        public static void Fatal(Exception exception, string messageTemplate, params object[] propertyValues) {
            Log.Fatal(exception, messageTemplate, propertyValues);
        }

        public static bool IsEnabled(LogEventLevel level) {
            return Log.IsEnabled(level);
        }
    }

    public class NetworkLogger {
        private readonly ILogger _logger = Log.ForContext("Category", "Network");

        public void Debug(string messageTemplate, params object[] propertyValues) {
            _logger.Debug(messageTemplate, propertyValues);
        }

        public void Info(string messageTemplate, params object[] propertyValues) {
            _logger.Information(messageTemplate, propertyValues);
        }

        public void Warning(string messageTemplate, params object[] propertyValues) {
            _logger.Warning(messageTemplate, propertyValues);
        }

        public void Error(Exception exception, string messageTemplate, params object[] propertyValues) {
            _logger.Error(exception, messageTemplate, propertyValues);
        }

        public void Error(string messageTemplate, params object[] propertyValues) {
            _logger.Error(messageTemplate, propertyValues);
        }
    }

    public class PropsLogger {
        private readonly ILogger _logger = Log.ForContext("Category", "Props");

        public void Debug(string messageTemplate, params object[] propertyValues) {
            _logger.Debug(messageTemplate, propertyValues);
        }

        public void Info(string messageTemplate, params object[] propertyValues) {
            _logger.Information(messageTemplate, propertyValues);
        }

        public void Warning(string messageTemplate, params object[] propertyValues) {
            _logger.Warning(messageTemplate, propertyValues);
        }

        public void Error(Exception exception, string messageTemplate, params object[] propertyValues) {
            _logger.Error(exception, messageTemplate, propertyValues);
        }

        public void Error(string messageTemplate, params object[] propertyValues) {
            _logger.Error(messageTemplate, propertyValues);
        }
    }

    public class RoomsLogger {
        private readonly ILogger _logger = Log.ForContext("Category", "Rooms");

        public void Debug(string messageTemplate, params object[] propertyValues) {
            _logger.Debug(messageTemplate, propertyValues);
        }

        public void Info(string messageTemplate, params object[] propertyValues) {
            _logger.Information(messageTemplate, propertyValues);
        }

        public void Warning(string messageTemplate, params object[] propertyValues) {
            _logger.Warning(messageTemplate, propertyValues);
        }

        public void Error(Exception exception, string messageTemplate, params object[] propertyValues) {
            _logger.Error(exception, messageTemplate, propertyValues);
        }

        public void Error(string messageTemplate, params object[] propertyValues) {
            _logger.Error(messageTemplate, propertyValues);
        }
    }

    public class PlayerLogger {
        private readonly ILogger _logger = Log.ForContext("Category", "Player");

        public void Debug(string messageTemplate, params object[] propertyValues) {
            _logger.Debug(messageTemplate, propertyValues);
        }

        public void Info(string messageTemplate, params object[] propertyValues) {
            _logger.Information(messageTemplate, propertyValues);
        }

        public void Warning(string messageTemplate, params object[] propertyValues) {
            _logger.Warning(messageTemplate, propertyValues);
        }

        public void Error(Exception exception, string messageTemplate, params object[] propertyValues) {
            _logger.Error(exception, messageTemplate, propertyValues);
        }

        public void Error(string messageTemplate, params object[] propertyValues) {
            _logger.Error(messageTemplate, propertyValues);
        }
    }

    public class SystemLogger {
        private readonly ILogger _logger = Log.ForContext("Category", "System");

        public void Debug(string messageTemplate, params object[] propertyValues) {
            _logger.Debug(messageTemplate, propertyValues);
        }

        public void Info(string messageTemplate, params object[] propertyValues) {
            _logger.Information(messageTemplate, propertyValues);
        }

        public void Warning(string messageTemplate, params object[] propertyValues) {
            _logger.Warning(messageTemplate, propertyValues);
        }

        public void Error(Exception exception, string messageTemplate, params object[] propertyValues) {
            _logger.Error(exception, messageTemplate, propertyValues);
        }

        public void Error(string messageTemplate, params object[] propertyValues) {
            _logger.Error(messageTemplate, propertyValues);
        }
    }
}
