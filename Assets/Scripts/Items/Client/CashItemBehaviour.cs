/// <summary>
/// Comportement client spécifique à la liasse de billets : ajoute le montant répliqué
/// (lu par la tooltip d'inventaire et le bouton « Déposer » de l'ATM) au comportement d'item
/// générique <see cref="ItemBehaviour"/>. On SOUS-CLASSE plutôt que de polluer ItemBehaviour
/// avec un besoin propre à un seul item (même pattern que <see cref="FuelCanisterBehaviour"/>).
/// Le prefab de la liasse porte ce composant à la place d'un ItemBehaviour nu.
///
/// Le montant est poussé par <c>ServerItemManager</c> via <c>S2C_ItemCash</c>, routé par
/// <c>ClientItemManager</c>.
/// </summary>
public class CashItemBehaviour : ItemBehaviour {
    /// <summary>Montant de la liasse (BC), répliqué depuis le serveur. 0 tant que le
    /// S2C_ItemCash n'est pas arrivé.</summary>
    public int Amount { get; private set; }

    public void SetAmount(int amount) => Amount = amount;
}
