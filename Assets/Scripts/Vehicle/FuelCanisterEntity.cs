/// <summary>
/// Entité serveur spécifique au bidon d'essence : ajoute une réserve de carburant à l'item-monde
/// générique <see cref="ItemEntity"/>. On SOUS-CLASSE plutôt que de polluer <see cref="ItemEntity"/>
/// avec un besoin propre à un seul item.
///
/// Instanciée par <c>ServerItemManager.SpawnItem</c> quand le configId est celui du bidon.
/// </summary>
public class FuelCanisterEntity : ItemEntity {
    /// <summary>Carburant restant dans le bidon (litres).</summary>
    public float Fuel;
}
