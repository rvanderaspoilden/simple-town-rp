/// <summary>
/// Identité du bidon d'essence (item de ravitaillement). La CAPACITÉ est désormais éditable dans
/// l'asset via <see cref="FuelCanisterConfig.fuelCapacity"/> (pas une constante). Cette classe ne
/// porte plus que l'id de config, partagé par le gate de l'action véhicule, le marqueur de spawn
/// et la consommation serveur.
///
/// Namespace global, cohérent avec les autres types item/véhicule.
/// </summary>
public static class FuelCanister {
    /// <summary>Id de l'ItemConfig du bidon (libres : 8 Cardboard, 100 débris, 101 TrashBag).</summary>
    public const int ConfigId = 102;

    /// <summary>Capacité configurée du bidon (litres), lue depuis l'asset FuelCanisterConfig.
    /// Repli 20 L si la config est absente.</summary>
    public static float Capacity() {
        var cfg = Sim.DatabaseManager.GetItemConfigById(ConfigId) as FuelCanisterConfig;
        return cfg != null ? cfg.fuelCapacity : 20f;
    }
}
