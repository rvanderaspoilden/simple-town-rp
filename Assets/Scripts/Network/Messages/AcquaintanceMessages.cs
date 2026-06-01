using Mirror;

// "Make acquaintance" social flow. Global namespace (like all other network
// messages) so Mirror's reflection-based handler registration matches.

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
    public byte state; // RelationshipState
}

/// <summary>Server → all clients: play the greeting animation on the given player.</summary>
public struct S2C_PlayGreet : NetworkMessage {
    public uint netId;
}
