namespace Sim.Jobs {
    public enum JobStatus : byte {
        Available,
        Offered,
        Active,
        Completed,
        Failed,
        Abandoned,
        Expired
    }

    public enum JobFailureReason : byte {
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
