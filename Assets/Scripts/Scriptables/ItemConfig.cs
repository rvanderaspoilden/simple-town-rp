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
    private PickableType type;

    [SerializeField]
    private List<Action> actions;

    [SerializeField]
    private Item prefab;

    public int ID => id;

    public string Label => label;

    public PickableType Type => type;

    public List<Action> Actions => actions;

    public Item Prefab => prefab;
}
