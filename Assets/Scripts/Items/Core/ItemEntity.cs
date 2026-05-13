using UnityEngine;

/// <summary>
/// Authoritative server-side record of a world item.
/// Plain data — no MonoBehaviour, no Mirror, no Unity lifecycle.
/// </summary>
public class ItemEntity
{
    public int    EntityId;
    public string RoomId;
    public int    ItemConfigId;
    public Vector3    Position;
    public Quaternion Rotation;

    // Hand state — zeroed when not held
    public uint     HolderNetId;      // 0 = not held
    public HandType HolderHand;
    public Vector3    LocalPosition;
    public Quaternion LocalRotation;

    /// <summary>Si non zéro, seul ce netId peut ramasser l'item (anti-vol mission).</summary>
    public uint AuthorizedNetId;

    /// <summary>
    /// Si false, l'item ne sera jamais persisté en DB (ni au pickup ni au drop)
    /// et donc pas restauré au reconnect. Utilisé par les items mission (colis,
    /// outils temporaires) qui doivent disparaître si le joueur déconnecte.
    /// Défaut true pour préserver le comportement existant.
    /// </summary>
    public bool Persistent = true;

    public bool IsHeld => HolderNetId != 0;
}
