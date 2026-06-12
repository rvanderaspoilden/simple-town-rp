using Mirror;

// 1-to-1 voice call signaling. Global namespace (like all other network messages)
// so Mirror's reflection-based handler registration matches.

/// <summary>Caller → server: start a call to a contact (by id; recipient may be offline).</summary>
public struct C2S_CallInvite : NetworkMessage {
    public string targetCharacterId;
}

/// <summary>Callee → server: accept the incoming call from callerCharacterId.</summary>
public struct C2S_CallAccept : NetworkMessage {
    public string callerCharacterId;
}

/// <summary>Callee → server: decline the incoming call from callerCharacterId.</summary>
public struct C2S_CallDecline : NetworkMessage {
    public string callerCharacterId;
}

/// <summary>Either participant → server: hang up (cancels ringing or ends an active call).</summary>
public struct C2S_CallHangup : NetworkMessage {
}

/// <summary>Server → callee: an incoming call. callerNetId resolves the Dissonance peer.</summary>
public struct S2C_IncomingCall : NetworkMessage {
    public string callerCharacterId;
    public string callerName;
    public uint callerNetId;
}

/// <summary>Server → caller: the target is ringing.</summary>
public struct S2C_CallRinging : NetworkMessage {
    public string calleeCharacterId;
    public string calleeName;
}

/// <summary>Server → both: the call was accepted; open the voice channel.
/// peerNetId resolves the other participant's Dissonance player.</summary>
public struct S2C_CallAccepted : NetworkMessage {
    public string peerCharacterId;
    public string peerName;
    public uint peerNetId;
}

/// <summary>Server → relevant party: the call ended or failed (reason = CallEndReason).</summary>
public struct S2C_CallEnded : NetworkMessage {
    public byte reason;
}
