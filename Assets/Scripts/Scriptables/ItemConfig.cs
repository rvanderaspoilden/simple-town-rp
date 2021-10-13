using System.Collections.Generic;
using Sim.Interactables;
using UnityEngine;

[CreateAssetMenu(menuName = "Configurations/Item", fileName = "New Item")]
public class ItemConfig : ScriptableObject {
    [SerializeField]
    private int id;
    
    [SerializeField]
    private string label;

    [SerializeField]
    private ItemType type;

    [SerializeField]
    private ItemHandleType handleType;

    [SerializeField]
    private List<Action> unEquippedActions;

    [SerializeField]
    private List<Action> equippedActions;

    [SerializeField]
    private Item prefab;

    [SerializeField]
    private Texture2D cursor;

    [SerializeField]
    private Sprite icon;

    public int ID => id;

    public string Label => label;

    public ItemType Type => type;

    public ItemHandleType HandleType => handleType;

    public List<Action> UnEquippedActions => unEquippedActions;

    public List<Action> EquippedActions => equippedActions;

    public Item Prefab => prefab;

    public Texture2D Cursor => cursor;

    public Sprite Icon => icon;
}
