namespace Sim.Missions {
    /// <summary>
    /// Rôle sémantique d'un MissionPoint dans le monde. Utilisé par les
    /// providers pour filtrer les points éligibles selon le métier.
    /// `Any` accepte n'importe quel rôle dans le tirage — utile pour des
    /// points polyvalents (hubs).
    /// </summary>
    public enum PointRole : byte {
        Any      = 0,
        Pickup   = 1,
        Delivery = 2,
        Trash    = 3,
        Cart     = 4,
        City     = 5,
    }
}
