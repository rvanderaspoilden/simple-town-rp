using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using UnityEngine;

namespace Sim.Logging {
    public sealed class UnityEditorSink : ILogEventSink {
        private readonly ITextFormatter _formatter;

        public UnityEditorSink() {
            _formatter = new MessageTemplateTextFormatter("[{Category}] {Message:lj}{NewLine}{Exception}");
        }

        public void Emit(LogEvent logEvent) {
            using var writer = new StringWriter();
            _formatter.Format(logEvent, writer);
            string text = writer.ToString().TrimEnd();

            switch (logEvent.Level) {
                case LogEventLevel.Warning:
                    Debug.LogWarning(text);
                    break;
                case LogEventLevel.Error:
                case LogEventLevel.Fatal:
                    Debug.LogError(text);
                    break;
                default:
                    Debug.Log(text);
                    break;
            }
        }
    }
}
