using Sim.NPC;
using UnityEngine;

/// <summary>
/// Point de spawn ("maison") d'un NPC. À placer dans la scène City.
/// Chaque NPC qui sort de ce point conserve sa référence comme "home".
/// Le <see cref="NpcSpawnManager"/> garde un slot occupé tant qu'un NPC est
/// vivant pour ce point (évite le double-spawn).
///
/// MARCHAND — si <see cref="merchantConfig"/> est assigné, ce point devient un « stand » :
/// le NPC qui en sort tient le stand (interactable, vend des items). Le transform du point
/// EST le stand. Friction designer minimale : poser le point, assigner un asset MerchantConfig.
/// </summary>
public class NpcSpawnPoint : MonoBehaviour {
    [Tooltip("Prefab NPC associé à ce point. Peut être laissé vide pour utiliser le prefab par défaut du SpawnManager.")]
    [SerializeField] private GameObject npcPrefab;

    [Tooltip("PrefabId tel que référencé dans NpcPrefabDatabase côté client.")]
    [SerializeField] private string prefabId = "default";

    [Tooltip("Optionnel. Si assigné, le NPC issu de ce point est un MARCHAND attitré à ce stand.")]
    [SerializeField] private MerchantConfig merchantConfig;

    public GameObject NpcPrefab => npcPrefab;
    public string     PrefabId  => prefabId;
    public Vector3    Position  => transform.position;
    public Quaternion Rotation  => transform.rotation;

    public MerchantConfig MerchantConfig => merchantConfig;
    public bool           IsMerchant     => merchantConfig != null;

    /// <summary>True si un NPC est actuellement vivant pour ce point.</summary>
    public bool IsOccupied { get; set; }

    private void OnEnable()  => NpcSpawnManager.Instance.RegisterSpawnPoint(this);
    private void OnDisable() => NpcSpawnManager.Instance.UnregisterSpawnPoint(this);

    private void OnDrawGizmos() {
        Gizmos.color = IsOccupied ? new Color(1f, 0.5f, 0.2f, 0.7f) : new Color(0.3f, 1f, 0.3f, 0.7f);
        Gizmos.DrawSphere(transform.position, 0.5f);
        Gizmos.DrawRay(transform.position, transform.forward * 1.2f);
    }
}
