using Sim;
using Sim.Enums;
using Sim.Interactables;

public class Consumable : Item {
    protected override void Execute(Action action) {
        if (action.Type == ActionTypeEnum.EAT || action.Type == ActionTypeEnum.DRINK) {
            PlayerController.Local.Consume(this);
        }
    }
}