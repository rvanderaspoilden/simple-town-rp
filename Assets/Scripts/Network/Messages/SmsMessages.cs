using Mirror;

// Direct-message ("SMS") flow. Global namespace (like all other network messages)
// so Mirror's reflection-based handler registration matches.

/// <summary>Client → server: send an SMS to a character (by id; recipient may be offline).</summary>
public struct C2S_SendSms : NetworkMessage {
    public string recipientCharacterId;
    public string text;
}

/// <summary>Server → recipient (if online): a new SMS arrived.</summary>
public struct S2C_SmsReceived : NetworkMessage {
    public string senderCharacterId;
    public string senderName;
    public string message;
    public long createdAt;
}

/// <summary>Client → server: the local player just read the conversation with
/// otherCharacterId (relay a read-receipt to that contact if online).</summary>
public struct C2S_SmsMarkRead : NetworkMessage {
    public string otherCharacterId;
}

/// <summary>Server → original sender (if online): readerCharacterId read the
/// messages you sent them — flip your sent bubbles to "Lu" in real time.</summary>
public struct S2C_SmsRead : NetworkMessage {
    public string readerCharacterId;
}
