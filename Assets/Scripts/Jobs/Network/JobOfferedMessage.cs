using Mirror;
using Sim.Jobs;

/// <summary>
/// Server → Client. Notifie le owner d'une mission qui lui est attribuée
/// (soit Offer direct, soit Take depuis le board). Le statusByte distingue
/// les deux flux : Offered (à accepter) vs Active (déjà en cours).
/// </summary>
public struct JobOfferedMessage : NetworkMessage {
    public string instanceId;
    public string jobId;
    public byte statusByte;
    public int currentStepIndex;
    public string currentPromptKey;
    public string currentTargetId;
    public string currentTargetName;

    public JobTargetKind primaryTargetKind;
    public string primaryTargetId;
    public string primaryTargetName;
    public JobTargetKind secondaryTargetKind;
    public string secondaryTargetId;
    public string secondaryTargetName;

    public string payloadItemId;

    // Elapsed time on the active timer at the moment the server sent this
    // message. The client tracks its own clock from receipt to compute the
    // remaining time without an active SyncVar. 0 when status is Offered.
    public float elapsedSeconds;

    public JobStatus Status => (JobStatus)statusByte;
}
