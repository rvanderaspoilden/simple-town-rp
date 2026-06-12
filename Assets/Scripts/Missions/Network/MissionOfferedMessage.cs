using Mirror;
using Sim.Missions;

/// <summary>
/// Server → Client. Notifie le owner d'une mission qui lui est attribuée
/// (soit Offer direct, soit Take depuis le board). Le statusByte distingue
/// les deux flux : Offered (à accepter) vs Active (déjà en cours).
/// </summary>
public struct MissionOfferedMessage : NetworkMessage {
    public string instanceId;
    public string missionId;
    public byte statusByte;
    public int currentStepIndex;
    public string currentPromptKey;
    public string currentTargetId;
    public string currentTargetName;

    /// <summary>Le beacon monde du MissionPoint doit-il s'allumer pour ce step
    /// (steps de navigation Reach/Deliver uniquement).</summary>
    public bool showTargetBeacon;

    public MissionTargetKind primaryTargetKind;
    public string primaryTargetId;
    public string primaryTargetName;
    public MissionTargetKind secondaryTargetKind;
    public string secondaryTargetId;
    public string secondaryTargetName;

    public string payloadItemId;

    // Elapsed time on the active timer at the moment the server sent this
    // message. The client tracks its own clock from receipt to compute the
    // remaining time without an active SyncVar. 0 when status is Offered.
    public float elapsedSeconds;

    public MissionStatus Status => (MissionStatus)statusByte;
}
