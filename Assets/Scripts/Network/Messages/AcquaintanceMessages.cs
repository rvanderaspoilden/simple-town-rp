using Mirror;

// "Make acquaintance" social flow. Global namespace (like all other network
// messages) so Mirror's reflection-based handler registration matches.

/// <summary>Which kind of relationship request a popup represents.</summary>
public enum AcquaintanceRequestKind { Acquaintance, Contact }

/// <summary>Client A → server: ask to make acquaintance with the player owning targetNetId.</summary>
public struct C2S_AcquaintanceRequest : NetworkMessage {
    public uint targetNetId;
}

/// <summary>Server → client B: an acquaintance request arrived from fromNetId
/// (B can read appearance/mood from that netId, never the name).</summary>
public struct S2C_AcquaintanceRequest : NetworkMessage {
    public uint fromNetId;
}

/// <summary>Client B → server: B's answer to A's request.</summary>
public struct C2S_AcquaintanceResponse : NetworkMessage {
    public uint fromNetId;
    public bool accepted;
}

/// <summary>Server → client A: feedback when the request was refused.</summary>
public struct S2C_AcquaintanceResult : NetworkMessage {
    public bool accepted;
}

/// <summary>Server → client: reveals an identity and updates the local relationship store.</summary>
public struct S2C_RelationshipUpdate : NetworkMessage {
    public string otherCharacterId;
    public string otherFullName;
    public byte state;        // RelationshipState
    public string jobProfessionId;   // other character's current profession id; "" = none
    public string metAt;      // ISO timestamp of first meeting
    public bool online;       // other character's online presence
}

/// <summary>Server → all clients: play the greeting animation on the given player.</summary>
public struct S2C_PlayGreet : NetworkMessage {
    public uint netId;
}

/// <summary>Server → all clients: a character's online presence changed.
/// Recipients update the relationship store; UIs subscribed to
/// ClientRelationshipManager.OnPresenceChanged refresh accordingly.</summary>
public struct S2C_ContactPresence : NetworkMessage {
    public string characterId;
    public bool online;
}

// ── "Add to contacts" flow (acquaintance → contact). Parallel to the
//    acquaintance messages; the server handler logic is shared. ───────────────

/// <summary>Client A → server: ask to add the player owning targetNetId to contacts.</summary>
public struct C2S_ContactRequest : NetworkMessage {
    public uint targetNetId;
}

/// <summary>Server → client B: a contact request arrived from fromNetId.</summary>
public struct S2C_ContactRequest : NetworkMessage {
    public uint fromNetId;
}

/// <summary>Client B → server: B's answer to A's contact request.</summary>
public struct C2S_ContactResponse : NetworkMessage {
    public uint fromNetId;
    public bool accepted;
}

/// <summary>Server → client A: feedback when the contact request was refused.</summary>
public struct S2C_ContactResult : NetworkMessage {
    public bool accepted;
}

// ── "Remove contact" flow. Full removal (relationship row + conversation),
//    mutual by design (single canonical relationship row). ─────────────────────

/// <summary>Client A → server: remove the relationship with characterId entirely.</summary>
public struct C2S_RemoveContact : NetworkMessage {
    public string characterId;
}

/// <summary>Server → affected client(s): the relationship with otherCharacterId was removed.</summary>
public struct S2C_RelationshipRemoved : NetworkMessage {
    public string otherCharacterId;
}
