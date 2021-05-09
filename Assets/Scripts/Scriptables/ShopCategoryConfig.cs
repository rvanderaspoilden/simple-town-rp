using Sim.Enums;
using UnityEngine;

[CreateAssetMenu(fileName = "New Shop Category", menuName = "Configurations/ShopCategoryConfig")]
public class ShopCategoryConfig : ScriptableObject {
    [SerializeField]
    private string categoryName;
    
    [SerializeField]
    private PropsType propsType;

    public string CategoryName => categoryName;

    public PropsType PropsType => propsType;
}
