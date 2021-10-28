using System.Collections.Generic;
using Sim.Scriptables;
using UnityEngine;

[CreateAssetMenu(fileName = "New Dispenser Config", menuName = "Configurations/Dispenser")]
public class DispenserConfiguration : PropsConfig {
    [SerializeField]
    private List<ItemPrice> itemsToSell;

    public List<ItemPrice> ItemsToSell => itemsToSell;
}
