using Mirror;
using Sim;
using Sim.Enums;
using Sim.Interactables;
using Action = Sim.Interactables.Action;

/// <summary>
/// Item tenu en main qui est lui-même un conteneur (« package »). L'action OPEN
/// (action équipée, déclenchée depuis le HUD d'inventaire) ouvre la grille — même
/// panneau que les conteneurs de prop (<see cref="ContainerPanelUI"/>), pilotée par
/// le PlaceId backend "item_container:{uuid}".
///
/// L'item doit être persisté (UUID DB) pour avoir une place ; le serveur refuse
/// l'ouverture sinon (S2C_ItemContainerOpenFailed).
/// </summary>
public class PackageItemBehaviour : ItemBehaviour
{
    /// <summary>
    /// Émis sur le client opener au clic OPEN, AVANT l'aller-retour serveur, pour que
    /// <see cref="ContainerPanelUI"/> ouvre le panneau optimistement.
    /// Args : entityId du package, slotCount, displayName (titre, fallback générique si vide).
    /// </summary>
    public static event System.Action<int, int, string> OnItemContainerOpenRequested;

    protected override void HandleSpecialAction(Action action)
    {
        if (action.Type == ActionTypeEnum.OPEN)
        {
            SendOpenRequest();
            return;
        }
        base.HandleSpecialAction(action);
    }

    private void SendOpenRequest()
    {
        if (!NetworkClient.isConnected) return;
        int slotCount = Configuration?.Container != null ? Configuration.Container.SlotCount : 0;
        if (slotCount <= 0) return;

        string displayName = Configuration != null ? Configuration.Label : null;
        OnItemContainerOpenRequested?.Invoke(Identity.EntityId, slotCount, displayName);
        NetworkClient.Send(new C2S_OpenItemContainer { EntityId = Identity.EntityId });
    }
}
