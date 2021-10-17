using Sim;
using Sim.Enums;
using Sim.Interactables;

public class Consumable : Item {
    protected override void Execute(Action action) {
        if (action.Type == ActionTypeEnum.EAT) {
            PlayerController.Local.Eat(this);
        }
    }
}