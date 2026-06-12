namespace Sim.Missions {
    public enum MissionStatus : byte {
        Available,
        Offered,
        Active,
        Completed,
        Failed,
        Abandoned,
        Expired
    }

    public enum MissionFailureReason : byte {
        None,
        Expired,
        TargetLost,
        Rejected,
        MinigameFailed,
        OwnerDisconnected,
        Cancelled
    }

    public enum StepStatus : byte {
        Pending,
        Running,
        Succeeded,
        Failed
    }
}
