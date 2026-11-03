using Sim.NPC;
using UnityEngine;

/// <summary>
/// Point de spawn ("maison") d'un NPC. À placer dans la scène City.
/// Chaque NPC qui sort de ce point conserve sa référence comme "home".
/// Le <see cref="NpcSpawnManager"/> garde un slot occupé tant qu'un NPC est
/// vivant pour ce point (évite le double-spawn).
///
/// CONFIG — l'unique champ à renseigner est <see cref="npcConfig"/> : il porte les prefabs
/// (serveur + client), le dialogue et l'éventuelle nature MARCHANDE (via la sous-classe
/// <see cref="MerchantNpcConfig"/>). Vide → le NPC adopte le NpcConfig « default » (passant
/// standard). Un point marchand devient un « stand » : le NPC tient le stand, interactable, vend des
/// items. Le transform du point EST le stand. Friction designer minimale : poser le point, assigner
/// un asset NpcConfig.
/// </summary>
public class NpcSpawnPoint : MonoBehaviour {
    [Tooltip("Config consolidée du NPC (prefabs + dialogue + éventuelle nature marchande). " +
             "Vide → NPC passant standard (config « default »).")]
    [SerializeField] private NpcConfig npcConfig;

    public NpcConfig  NpcConfig => npcConfig;
    public Vector3    Position  => transform.position;
    public Quaternion Rotation  => transform.rotation;

    /// <summary>True si un NPC est actuellement vivant pour ce point.</summary>
    public bool IsOccupied { get; set; }

    private void OnEnable()  => NpcSpawnManager.Instance.RegisterSpawnPoint(this);
    private void OnDisable() => NpcSpawnManager.Instance.UnregisterSpawnPoint(this);

    private void OnDrawGizmos() {
        Gizmos.color = IsOccupied ? new Color(1f, 0.5f, 0.2f, 0.7f) : new Color(0.3f, 1f, 0.3f, 0.7f);
        Gizmos.DrawSphere(transform.position, 0.5f);
        Gizmos.DrawRay(transform.position, transform.forward * 1.2f);

        // Point hostile : sphère rouge par-dessus pour le repérer d'un coup d'œil en scène.
        if (npcConfig != null && npcConfig.IsHostile) {
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.8f, 0.25f);
        }
    }

    /// <summary>Rayons d'aggro du point hostile (sélection) : zone défendue (rouge),
    /// détection (orange), patrouille (jaune).</summary>
    private void OnDrawGizmosSelected() {
        if (!(npcConfig is HostileNpcConfig hostile)) return;
        Vector3 p = transform.position;
        Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.8f);
        Gizmos.DrawWireSphere(p, hostile.ZoneRadius);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(p, hostile.AggroRadius);
        Gizmos.color = new Color(1f, 0.95f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(p, hostile.PatrolRadius);
    }
}
