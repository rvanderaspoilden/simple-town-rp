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
        if (action.Type == ActionTypeEnum.DRINK)
        {
            // Play the drink gesture on the hand that holds THIS item; consumed by the
            // OnDrinkSip animation event when the cup reaches the mouth.
            PlayerController.Local.Drink(Identity.EntityId, HolderHand);
        }
        else if (action.Type == ActionTypeEnum.EAT)
        {
            // Eat gesture on the hand that holds THIS item; consumed by the OnEatBite animation
            // event when the food reaches the mouth. Reuses the drink animation for now.
            PlayerController.Local.Eat(Identity.EntityId, HolderHand);
        }
    }
}
