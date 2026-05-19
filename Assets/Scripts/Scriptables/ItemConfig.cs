using System.Collections.Generic;
using Sim;
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
    private SortingCategory sortingCategory;

    [SerializeField]
    private List<Action> unEquippedActions;

    [SerializeField]
    private List<Action> equippedActions;

    [SerializeField]
    private ItemBehaviour prefab;

    [SerializeField]
    private Texture2D cursor;

    [SerializeField]
    private Sprite icon;

    [Header("Carry pose")]
    [Tooltip("Override the generic 1H/2H pose with a specific one (TRAY, MUG, …). Leave NONE to fall back on HandleType-driven generic pose.")]
    [SerializeField]
    private CarryPose pose = CarryPose.NONE;

    [Tooltip("Local position of the item relative to the hand bone it is parented to.")]
    [SerializeField]
    private Vector3 gripPosition;

    [Tooltip("Local rotation (euler) of the item relative to the hand bone it is parented to.")]
    [SerializeField]
    private Vector3 gripEuler;

    public int ID => id;

    public string Label => label;

    public ItemType Type => type;

    public ItemHandleType HandleType => handleType;

    public SortingCategory SortingCategory => sortingCategory;

    public List<Action> UnEquippedActions => unEquippedActions;

    public List<Action> EquippedActions => equippedActions;

    public ItemBehaviour Prefab => prefab;

    public Texture2D Cursor => cursor;

    public Sprite Icon => icon;

    public CarryPose Pose => pose;

    public Vector3 GripPosition => gripPosition;

    public Vector3 GripEuler => gripEuler;

    public bool HasGripOverride => gripPosition != Vector3.zero || gripEuler != Vector3.zero;
}
