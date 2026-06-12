using Mirror;

// "Give money" social flow. Global namespace (like all other network messages)
// so Mirror's reflection-based handler registration matches.

/// <summary>Client → server: give `amount` BC to the player owning `targetNetId`.
/// Server validates funds, posts a ledger entry on each side, then notifies both
/// players via the existing ToastNotificationMessage / BANK channel.</summary>
public struct C2S_GiveMoney : NetworkMessage {
    public uint targetNetId;
    public int  amount;
}
