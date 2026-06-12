namespace Sim.Missions {
    /// <summary>
    /// État runtime d'un step. Server-side uniquement. Pilote sa propre
    /// progression via OnEnter / Tick / OnExit, et signale au job qu'il doit
    /// avancer (Succeed) ou échouer (Fail) via les helpers protégés.
    /// </summary>
    public abstract class MissionStepInstance {
        protected readonly MissionInstance job;
        public StepStatus Status { get; protected set; } = StepStatus.Pending;

        protected MissionStepInstance(MissionInstance job) {
            this.job = job;
        }

        public abstract void OnEnter();
        public abstract void Tick(float dt);
        public abstract void OnExit();

        protected void Succeed() {
            if (Status != StepStatus.Running) return;
            Status = StepStatus.Succeeded;
            job.AdvanceStep();
        }

        protected void Fail(MissionFailureReason reason) {
            if (Status != StepStatus.Running) return;
            Status = StepStatus.Failed;
            job.Fail(reason);
        }

        internal void MarkRunning() => Status = StepStatus.Running;
    }
}
