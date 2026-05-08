using Sim;
using Sim.Enums;
using Sim.Interactables;
using Action = Sim.Interactables.Action;

/// <summary>
/// Item variant that can be eaten or drunk.
/// Extends ItemBehaviour — just overrides action handling for EAT/DRINK.
/// </summary>
public class ConsumableItemBehaviour : ItemBehaviour
{
    protected override void HandleSpecialAction(Action action)
    {
        if (action.Type == ActionTypeEnum.EAT || action.Type == ActionTypeEnum.DRINK)
        {
            PlayerController.Local.ConsumeItem(Identity.EntityId);
        }
    }
}
