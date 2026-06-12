using System;

namespace Sim.Missions {
    /// <summary>
    /// Hub d'events serveur du système de jobs. Permet à RewardSystem,
    /// MissionServerManager (broadcast réseau) et providers d'observer le cycle de
    /// vie d'une mission sans couplage direct avec MissionInstance.
    /// Tous les events fire côté serveur uniquement.
    /// </summary>
    public static class MissionEvents {
        public static event Action<MissionInstance> MissionPublished;
        public static event Action<MissionInstance> MissionOffered;
        public static event Action<MissionInstance> MissionAccepted;
        public static event Action<MissionInstance> MissionTaken;
        public static event Action<MissionInstance> StepAdvanced;
        public static event Action<MissionInstance> MissionCompleted;
        public static event Action<MissionInstance> MissionFailed;

        internal static void RaiseMissionPublished(MissionInstance job) => MissionPublished?.Invoke(job);
        internal static void RaiseMissionOffered(MissionInstance job)   => MissionOffered?.Invoke(job);
        internal static void RaiseMissionAccepted(MissionInstance job)  => MissionAccepted?.Invoke(job);
        internal static void RaiseMissionTaken(MissionInstance job)     => MissionTaken?.Invoke(job);
        internal static void RaiseStepAdvanced(MissionInstance job) => StepAdvanced?.Invoke(job);
        internal static void RaiseMissionCompleted(MissionInstance job) => MissionCompleted?.Invoke(job);
        internal static void RaiseMissionFailed(MissionInstance job)    => MissionFailed?.Invoke(job);

        public static void ClearAllSubscriptions() {
            MissionPublished = null;
            MissionOffered = null;
            MissionAccepted = null;
            MissionTaken = null;
            StepAdvanced = null;
            MissionCompleted = null;
            MissionFailed = null;
        }
    }
}
