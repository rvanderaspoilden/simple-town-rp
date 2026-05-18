namespace Sim.Jobs {
    /// <summary>
    /// Clés sémantiques utilisées pour nommer un slot de cible dans JobContext.
    /// Exposé sous forme d'enum pour avoir un dropdown propre dans l'Inspector
    /// (zéro typo). Converti en string au runtime via JobTargetKeyExtensions.ToKey().
    ///
    /// Ajouter une nouvelle clé = nouvelle entrée ici, recompile, et l'option
    /// apparaît dans tous les step definitions automatiquement.
    /// </summary>
    public enum JobTargetKey : byte {
        Pickup   = 0,
        Delivery = 1,
        // Cleaner — réservés pour usage futur
        City     = 2,
        Trash    = 3,
        Cart     = 4,
    }

    public static class JobTargetKeyExtensions {
        public static string ToKey(this JobTargetKey k) => k switch {
            JobTargetKey.Pickup   => "pickup",
            JobTargetKey.Delivery => "delivery",
            JobTargetKey.City     => "city",
            JobTargetKey.Trash    => "trash",
            JobTargetKey.Cart     => "cart",
            _                     => "pickup"
        };
    }
}
