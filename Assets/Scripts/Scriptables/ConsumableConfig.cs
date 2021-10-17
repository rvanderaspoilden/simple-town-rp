using UnityEngine;

[CreateAssetMenu(fileName = "New Consumable", menuName = "Configurations/Item/Consumable")]
public class ConsumableConfig : ItemConfig {
    [SerializeField]
    private ConsumableType consumableType;

    [SerializeField]
    private HealthValue[] impacts;

    public ConsumableType ConsumableType => consumableType;

    public HealthValue[] Impacts => impacts;
}