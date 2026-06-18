using System.Collections.Generic;
using Sim;
using Sim.Interactables;
using Sim.Scriptables;
using UnityEngine;

[CreateAssetMenu(menuName = "Configurations/Item", fileName = "New Item")]
public class ItemConfig : ScriptableObject {
    [SerializeField]
    private int id;

    [SerializeField]
    private string label;

    [Tooltip("Description affichée dans la tooltip au survol d'un slot (optionnel).")]
    [SerializeField] [TextArea]
    private string description;

    [SerializeField]
    private ItemType type;

    [SerializeField]
    private ItemHandleType handleType;

    [SerializeField]
    private List<Action> unEquippedActions;

    [SerializeField]
    private List<Action> equippedActions;

    [SerializeField]
    private ItemBehaviour prefab;

    [Tooltip("Item persisté dans le monde : sauvegardé au sol avec sa position, rechargé à l'entrée de pièce, survit au redémarrage. Supprimé au ramassage. Ex : débris d'un prop détruit, futurs détritus de ville.")]
    [SerializeField]
    private bool toPersist;

    [Tooltip("Autorise le stockage en poche du joueur. Décocher pour les items qui ne peuvent rester qu'en main (déchets, colis mission, objets encombrants, etc.). Défaut: true.")]
    [SerializeField]
    private bool allowedInPocket = true;

    [SerializeField]
    private Texture2D cursor;

    [SerializeField]
    private Sprite icon;

    [Header("Carry pose")]
    [Tooltip("Hand-agnostic carry style (TRAY, MUG, BOX…). Leave NONE for the default 1H/2H grip. The resolver combines this with the runtime hand assignment to pick the animator pose.")]
    [SerializeField]
    private CarryShape poseShape = CarryShape.NONE;

    [Tooltip("Local position of the item relative to the hand bone it is parented to.")]
    [SerializeField]
    private Vector3 gripPosition;

    [Tooltip("Local rotation (euler) of the item relative to the hand bone it is parented to.")]
    [SerializeField]
    private Vector3 gripEuler;

    [Header("Stacking")]
    [Tooltip("Capacité max d'une pile dans un slot de conteneur (prop, colis, coffre véhicule). " +
             "1 = non empilable (défaut). >1 = empilable jusqu'à cette valeur. Mains et poches " +
             "restent à 1 unité par slot quelle que soit cette valeur (split-1 implicite à l'extraction).")]
    [SerializeField] [Min(1)]
    private int maxStackSize = 1;

    public int ID => id;

    public string Label => label;

    public string Description => description;

    public ItemType Type => type;

    public ItemHandleType HandleType => handleType;

    public List<Action> UnEquippedActions => unEquippedActions;

    public List<Action> EquippedActions => equippedActions;

    public ItemBehaviour Prefab => prefab;

    public bool ToPersist => toPersist;

    public bool AllowedInPocket => allowedInPocket;

    public Texture2D Cursor => cursor;

    public Sprite Icon => icon;

    public CarryShape PoseShape => poseShape;

    public Vector3 GripPosition => gripPosition;

    public Vector3 GripEuler => gripEuler;

    public bool HasGripOverride => gripPosition != Vector3.zero || gripEuler != Vector3.zero;

    public int MaxStackSize => Mathf.Max(1, maxStackSize);

    public bool IsStackable => MaxStackSize > 1;
}
