#if STRESS_TEST_BOTS
using System;
using System.Collections.Generic;

namespace Sim.StressTest {
    /// <summary>
    /// Lazy parser for the CLI flags consumed by the stress-test bot mode. Reads
    /// <see cref="Environment.GetCommandLineArgs"/> exactly once and caches the
    /// result so any boot-time code (LauncherManager, ApiManager.Awake, BotRunner)
    /// can query the same view without re-parsing.
    ///
    /// Supported formats: "--key=value" (preferred), "--key value" (positional),
    /// and "--flag" (boolean presence).
    /// </summary>
    public static class CommandLineArgs {
        private static readonly Dictionary<string, string> _values = Parse();

        public static bool BotMode => _values.ContainsKey("bot");
        public static int BotIndex => int.TryParse(GetValue("bot-index"), out var i) ? i : 0;
        public static string BotServer => GetValue("bot-server");
        public static string BotSecret => GetValue("bot-secret");

        public static string GetValue(string key) =>
            _values.TryGetValue(key, out var v) ? v : null;

        private static Dictionary<string, string> Parse() {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] args;
            try {
                args = Environment.GetCommandLineArgs();
            } catch {
                return dict;
            }

            for (int i = 0; i < args.Length; i++) {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg) || !arg.StartsWith("--")) continue;

                string keyValue = arg.Substring(2);
                int eq = keyValue.IndexOf('=');
                if (eq >= 0) {
                    dict[keyValue.Substring(0, eq)] = keyValue.Substring(eq + 1);
                    continue;
                }

                string nextValue = (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    ? args[++i]
                    : string.Empty;
                dict[keyValue] = nextValue;
            }
            return dict;
        }
    }
}
#endif
