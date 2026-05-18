using System;

namespace Sim.Jobs {
    /// <summary>
    /// Hub d'events serveur du système de jobs. Permet à RewardSystem,
    /// JobServerManager (broadcast réseau) et providers d'observer le cycle de
    /// vie d'une mission sans couplage direct avec JobInstance.
    /// Tous les events fire côté serveur uniquement.
    /// </summary>
    public static class JobEvents {
        public static event Action<JobInstance> JobPublished;
        public static event Action<JobInstance> JobOffered;
        public static event Action<JobInstance> JobAccepted;
        public static event Action<JobInstance> JobTaken;
        public static event Action<JobInstance> StepAdvanced;
        public static event Action<JobInstance> JobCompleted;
        public static event Action<JobInstance> JobFailed;

        internal static void RaiseJobPublished(JobInstance job) => JobPublished?.Invoke(job);
        internal static void RaiseJobOffered(JobInstance job)   => JobOffered?.Invoke(job);
        internal static void RaiseJobAccepted(JobInstance job)  => JobAccepted?.Invoke(job);
        internal static void RaiseJobTaken(JobInstance job)     => JobTaken?.Invoke(job);
        internal static void RaiseStepAdvanced(JobInstance job) => StepAdvanced?.Invoke(job);
        internal static void RaiseJobCompleted(JobInstance job) => JobCompleted?.Invoke(job);
        internal static void RaiseJobFailed(JobInstance job)    => JobFailed?.Invoke(job);

        public static void ClearAllSubscriptions() {
            JobPublished = null;
            JobOffered = null;
            JobAccepted = null;
            JobTaken = null;
            StepAdvanced = null;
            JobCompleted = null;
            JobFailed = null;
        }
    }
}
