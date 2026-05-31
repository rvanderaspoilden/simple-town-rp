using System;
using UnityEngine;

namespace Sim.Deployment {
    /// <summary>
    /// Runtime singleton that remembers which environment the user picked in the
    /// Launcher dropdown. Persisted in PlayerPrefs across sessions so the next
    /// boot opens straight on the previously-selected env.
    ///
    /// Priority order (highest to lowest) for the values consumed by ApiManager
    /// and SimpleTownNetwork:
    ///   1. STRESS_TEST_BOTS CLI flags  (bot stress test, --bot-server)
    ///   2. Env vars  API_URL / MIRROR_HOST  (server-side deployment)
    ///   3. EnvironmentSelector.Current  (this — human player UI choice)
    ///   4. Hardcoded defaults  (localhost fallback)
    /// </summary>
    public static class EnvironmentSelector {
        private const string PREF_KEY = "sim.environment.selected";
        private const string DEFAULT_NAME = "Local";

        private static EnvironmentRegistry _registry;
        private static EnvironmentEntry _current;

        public static event Action<EnvironmentEntry> OnChanged;

        public static EnvironmentEntry Current {
            get {
                if (_current == null) Initialize();
                return _current;
            }
        }

        public static EnvironmentRegistry Registry {
            get {
                if (_registry == null) Initialize();
                return _registry;
            }
        }

        private static void Initialize() {
            _registry = EnvironmentRegistry.Load();
            string saved = PlayerPrefs.GetString(PREF_KEY, string.Empty);
            _current = ResolveOrDefault(saved);
        }

        /// <summary>
        /// Switches the active environment by name. No-op if the name is unknown.
        /// Persists the selection and notifies subscribers.
        /// </summary>
        public static void Select(string name) {
            if (_registry == null) Initialize();
            var resolved = ResolveOrDefault(name);
            if (resolved == null) return;
            if (_current != null && _current.Name == resolved.Name) return;
            _current = resolved;
            PlayerPrefs.SetString(PREF_KEY, resolved.Name);
            PlayerPrefs.Save();
            OnChanged?.Invoke(_current);
        }

        private static EnvironmentEntry ResolveOrDefault(string name) {
            if (_registry == null || _registry.Environments == null || _registry.Environments.Count == 0) {
                // Registry asset missing / empty: hardcoded localhost fallback so the
                // game still boots in editor without setup.
                return new EnvironmentEntry(
                    "Local (fallback)",
                    "http://localhost:3000",
                    "localhost",
                    "http://localhost:8080/health");
            }
            if (!string.IsNullOrEmpty(name)) {
                foreach (var env in _registry.Environments) {
                    if (env != null && env.Name == name) return env;
                }
            }
            // Prefer an entry literally named "Local" if present; otherwise the first one.
            foreach (var env in _registry.Environments) {
                if (env != null && env.Name == DEFAULT_NAME) return env;
            }
            return _registry.Environments[0];
        }

        /// <summary>
        /// Forces a refresh from the asset. Useful in the Editor when you tweak
        /// the registry while in Play mode.
        /// </summary>
        public static void Reload() {
            _registry = null;
            _current = null;
            Initialize();
        }
    }
}
