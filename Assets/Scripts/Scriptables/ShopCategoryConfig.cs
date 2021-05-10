using Sim.Enums;
using UnityEngine;

[CreateAssetMenu(fileName = "New Shop Category", menuName = "Configurations/ShopCategoryConfig")]
public class ShopCategoryConfig : ScriptableObject {
    [SerializeField]
    private string categoryName;

    [SerializeField]
    private ShopCategoryType categoryType;
    
    [SerializeField]
    private PropsType propsType;

    [SerializeField]
    private BuildSurfaceEnum coverType;
    
    public string CategoryName => categoryName;

    public PropsType PropsType => propsType;

    public ShopCategoryType CategoryType => categoryType;

    public BuildSurfaceEnum CoverType => coverType;
}
