using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Scriptables {
    /// <summary>
    /// Holds the four cross-cutting "sale" Actions injected dynamically onto any
    /// sellable prop (so we don't have to add them to every PropsConfig asset).
    ///
    /// Loaded once at runtime from Resources. Create the asset at
    /// <c>Assets/Resources/Configurations/Sale/SaleActionsConfig.asset</c> and
    /// assign the four Action ScriptableObjects (each with its icon/label and the
    /// matching ActionTypeEnum: LIST_FOR_SALE, GIVE, UNLIST, BUY).
    ///
    /// If the asset is absent, sale actions simply aren't injected (the rest of the
    /// prop system keeps working) — so this degrades gracefully until the designer
    /// wires the assets.
    /// </summary>
    [CreateAssetMenu(fileName = "SaleActionsConfig", menuName = "Configurations/Sale Actions Config")]
    public class SaleActionsConfig : ScriptableObject {
        public const string ResourcePath = "Configurations/Sale/SaleActionsConfig";

        [Tooltip("Owner action: list a placed prop for sale (opens the price input UI).")]
        public Action listForSale;

        [Tooltip("Owner action: give the prop away (lists it at price 0).")]
        public Action give;

        [Tooltip("Owner action: remove the prop from sale.")]
        public Action unlist;

        [Tooltip("Visitor action: buy a prop currently for sale (opens the confirm fiche).")]
        public Action buy;

        [Tooltip("Optional: prefab for the floating 'À vendre' billboard (with a PropSaleBillboard component). If empty, a default billboard is built procedurally at runtime.")]
        public GameObject billboardPrefab;

        private static SaleActionsConfig _cached;
        private static bool _loaded;

        /// <summary>Lazily loads the singleton config from Resources (null if absent).</summary>
        public static SaleActionsConfig Get() {
            if (!_loaded) {
                _cached = Resources.Load<SaleActionsConfig>(ResourcePath);
                _loaded = true;
            }
            return _cached;
        }
    }
}
