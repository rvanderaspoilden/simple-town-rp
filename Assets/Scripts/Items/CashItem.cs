/// <summary>
/// Identité de la liasse de billets (cash physique retiré à un ATM). Le MONTANT d'une liasse
/// est fixé à sa création (retrait) et ne change plus jamais : suivi côté serveur par
/// ServerItemManager (clé "e:{entityId}" puis uuid, persisté en stateData.cashAmount comme le
/// chargeur d'arme) et répliqué aux clients via S2C_ItemCash (tooltip + bouton Déposer).
///
/// Namespace global, cohérent avec <see cref="FuelCanister"/>.
/// </summary>
public static class CashItem {
    /// <summary>Id de l'ItemConfig de la liasse (derniers pris : 108 munitions, 109 Backpack).</summary>
    public const int ConfigId = 110;
}
