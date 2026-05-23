using UnityEngine;

/// <summary>
/// Mutable server-side record for one prop instance.
/// Payload is the serialized typed state (DoorState, SeatState, …).
///
/// IsScene = true  → prop already exists in the City scene on every client.
///                   Snapshot sends only S2C_PropUpdate (state only).
/// IsScene = false → prop was spawned at runtime (apartment furniture, etc.).
///                   Snapshot sends S2C_PropSpawn (full instantiation info).
/// </summary>
public class ServerPropState {
    public int        PropId;
    public int        PrefabId;   // PropsConfig.GetId()
    public string     RoomId;
    public Vector3    Position;
    public Quaternion Rotation;
    public PropType   Type;
    public byte[]     Payload;
    public bool       IsScene;

    // ── Sale state (player-to-player) ─────────────────────────────────────────
    // Orthogonal to the typed Payload. Persisted to the props row's for_sale/price
    // columns; the runtime mirrors them here so SendRoomSnapshot can rebroadcast.
    public bool   ForSale;
    public int    Price;
    public string OwnerCharId;   // apartment tenant — lets clients hide BUY from the owner

    // Magasin physique : prop d'exposition (City) marqué ShopDisplay. L'achat crée
    // une COPIE livrée à l'acheteur au lieu de transférer ce prop — l'expo reste en
    // place (stock infini). Distingue le flux shop du flux de vente P2P côté serveur.
    public bool   IsShopDisplay;

    // Reservation is transient (in-memory only) — it just guards the async buy
    // window against simultaneous buyers. Not persisted.
    public string ReservedByCharId;   // null = not reserved
    public string ReservedByName;     // display name for "Réservé à X"
    public double ReservedUntilUnix;  // unix seconds; reservation expires after this
}
