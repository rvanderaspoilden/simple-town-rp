namespace Sim.Enums {
    /// <summary>Why a voice call ended or failed. Carried in S2C_CallEnded.</summary>
    public enum CallEndReason : byte {
        PeerHangup = 0,   // the other participant hung up an active call
        Declined = 1,     // callee declined the incoming call
        Unavailable = 2,  // target offline / not reachable
        Busy = 3,         // caller or target already in a call
        Timeout = 4,      // ringing expired without an answer (missed call)
        Cancelled = 5,    // caller cancelled while it was still ringing
    }
}
