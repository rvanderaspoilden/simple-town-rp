/// <summary>
/// Comportement client spécifique au bidon d'essence : ajoute le niveau de carburant répliqué
/// (lu par la tooltip d'inventaire) au comportement d'item générique <see cref="ItemBehaviour"/>.
/// On SOUS-CLASSE plutôt que de polluer <see cref="ItemBehaviour"/> avec un besoin propre à un seul
/// item. Le prefab du bidon porte ce composant à la place d'un <see cref="ItemBehaviour"/> nu.
///
/// Le niveau est poussé par <c>ServerItemManager</c> via <c>S2C_ItemFuel</c>, routé par
/// <c>ClientItemManager</c>.
/// </summary>
public class FuelCanisterBehaviour : ItemBehaviour {
    /// <summary>Carburant restant (litres), répliqué depuis le serveur.</summary>
    public float Fuel { get; private set; }

    public void SetFuel(float fuel) => Fuel = fuel;
}
