using System;
using System.Collections.Generic;

namespace Sim.Jobs {
    /// <summary>
    /// État runtime d'une mission. Vit côté serveur uniquement. Toutes les
    /// transitions passent par les méthodes publiques de cette classe — pas de
    /// mutation directe des champs. Les clients reçoivent l'état via les
    /// NetworkMessage de Jobs.Network, pas via SyncVar.
    /// </summary>
    public class JobInstance {
        public string InstanceId { get; }
        public JobDefinition Definition { get; }
        public uint OwnerNetId { get; private set; }
        public JobContext Context { get; }
        public JobStatus Status { get; private set; }
        public JobFailureReason FailureReason { get; private set; }
        public int CurrentStepIndex { get; private set; }
        public float ElapsedSeconds { get; private set; }

        private readonly List<JobStepInstance> steps;

        public JobStepInstance CurrentStep =>
            CurrentStepIndex < steps.Count ? steps[CurrentStepIndex] : null;

        public bool IsAvailable => Status == JobStatus.Available;
        public bool HasOwner => OwnerNetId != 0u;

        private JobInstance(JobDefinition definition, uint ownerNetId, JobContext context, JobStatus initialStatus) {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            InstanceId = Guid.NewGuid().ToString("N");
            Definition = definition;
            OwnerNetId = ownerNetId;
            Context = context ?? new JobContext();
            Status = initialStatus;

            steps = new List<JobStepInstance>(definition.Steps.Count);
            for (int i = 0; i < definition.Steps.Count; i++) {
                var stepDef = definition.Steps[i];
                if (stepDef == null) continue;
                steps.Add(stepDef.CreateInstance(this));
            }
        }

        public static JobInstance CreateOffer(JobDefinition def, uint ownerNetId, JobContext ctx)
            => new JobInstance(def, ownerNetId, ctx, JobStatus.Offered);

        public static JobInstance CreatePublished(JobDefinition def, JobContext ctx)
            => new JobInstance(def, 0u, ctx, JobStatus.Available);

        public void Accept() {
            if (Status != JobStatus.Offered) return;
            Status = JobStatus.Active;
            ElapsedSeconds = 0f;
            EnterCurrentStep();
            JobEvents.RaiseJobAccepted(this);
        }

        public bool Take(uint newOwnerNetId) {
            if (Status != JobStatus.Available || newOwnerNetId == 0u) return false;
            OwnerNetId = newOwnerNetId;
            Status = JobStatus.Active;
            ElapsedSeconds = 0f;
            EnterCurrentStep();
            JobEvents.RaiseJobTaken(this);
            return true;
        }

        public void Tick(float dt) {
            if (Status == JobStatus.Available) {
                ElapsedSeconds += dt;
                if (Definition.BoardExpirationSeconds > 0f && ElapsedSeconds > Definition.BoardExpirationSeconds) {
                    FinalizeFail(JobFailureReason.Expired, JobStatus.Expired);
                }
                return;
            }

            if (Status != JobStatus.Active) return;

            ElapsedSeconds += dt;
            if (Definition.ExpirationSeconds > 0f && ElapsedSeconds > Definition.ExpirationSeconds) {
                FinalizeFail(JobFailureReason.Expired, JobStatus.Expired);
                return;
            }

            var step = CurrentStep;
            if (step == null) {
                Complete();
                return;
            }
            step.Tick(dt);
        }

        public void AdvanceStep() {
            if (Status != JobStatus.Active) return;
            CurrentStep?.OnExit();
            CurrentStepIndex++;
            if (CurrentStepIndex >= steps.Count) {
                Complete();
                return;
            }
            EnterCurrentStep();
            JobEvents.RaiseStepAdvanced(this);
        }

        public void Fail(JobFailureReason reason)   => FinalizeFail(reason, JobStatus.Failed);
        public void Abandon()                        => FinalizeFail(JobFailureReason.Cancelled, JobStatus.Abandoned);
        public void OwnerDisconnected()              => FinalizeFail(JobFailureReason.OwnerDisconnected, JobStatus.Failed);

        private void Complete() {
            if (Status != JobStatus.Active) return;
            CurrentStep?.OnExit();
            Status = JobStatus.Completed;
            JobEvents.RaiseJobCompleted(this);
        }

        private void FinalizeFail(JobFailureReason reason, JobStatus terminalStatus) {
            if (IsTerminal(Status)) return;
            if (Status == JobStatus.Active) CurrentStep?.OnExit();
            FailureReason = reason;
            Status = terminalStatus;
            JobEvents.RaiseJobFailed(this);
        }

        private void EnterCurrentStep() {
            var step = CurrentStep;
            if (step == null) return;
            step.MarkRunning();
            step.OnEnter();
        }

        private static bool IsTerminal(JobStatus s) =>
            s == JobStatus.Completed || s == JobStatus.Failed ||
            s == JobStatus.Abandoned || s == JobStatus.Expired;
    }
}
