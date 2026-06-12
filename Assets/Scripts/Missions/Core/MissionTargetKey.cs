namespace Sim.Missions {
    /// <summary>
    /// Clés sémantiques utilisées pour nommer un slot de cible dans MissionContext.
    /// Exposé sous forme d'enum pour avoir un dropdown propre dans l'Inspector
    /// (zéro typo). Converti en string au runtime via MissionTargetKeyExtensions.ToKey().
    ///
    /// Ajouter une nouvelle clé = nouvelle entrée ici, recompile, et l'option
    /// apparaît dans tous les step definitions automatiquement.
    /// </summary>
    public enum MissionTargetKey : byte {
        Pickup   = 0,
        Delivery = 1,
        // Cleaner — réservés pour usage futur
        City     = 2,
        Trash    = 3,
        Cart     = 4,
    }

    public static class MissionTargetKeyExtensions {
        public static string ToKey(this MissionTargetKey k) => k switch {
            MissionTargetKey.Pickup   => "pickup",
            MissionTargetKey.Delivery => "delivery",
            MissionTargetKey.City     => "city",
            MissionTargetKey.Trash    => "trash",
            MissionTargetKey.Cart     => "cart",
            _                     => "pickup"
        };
    }
}
