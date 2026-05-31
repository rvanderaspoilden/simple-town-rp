using System.Collections.Generic;
using UnityEngine;

namespace Sim.Deployment {
    /// <summary>
    /// Catalog of deployment environments the Launcher can switch between.
    /// Single asset placed at <c>Assets/Resources/Configurations/Environments/EnvironmentRegistry.asset</c>
    /// so it can be loaded via <c>Resources.Load</c> without scene references.
    ///
    /// Add a new entry to extend the dropdown (e.g. Staging) — no code change needed.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Environment Registry", fileName = "EnvironmentRegistry")]
    public class EnvironmentRegistry : ScriptableObject {
        [SerializeField] private List<EnvironmentEntry> environments = new List<EnvironmentEntry>();

        public IReadOnlyList<EnvironmentEntry> Environments => environments;

        /// <summary>
        /// Resolves the live registry from Resources. Returns null if the asset
        /// is missing — callers should fall back to a hardcoded "Local" default
        /// rather than throwing so a broken asset doesn't brick the Launcher.
        /// </summary>
        public static EnvironmentRegistry Load() {
            return Resources.Load<EnvironmentRegistry>("Configurations/Environments/EnvironmentRegistry");
        }
    }
}
