using System;
using System.Collections.Generic;

namespace Sim.Missions {
    /// <summary>
    /// État runtime d'une mission. Vit côté serveur uniquement. Toutes les
    /// transitions passent par les méthodes publiques de cette classe — pas de
    /// mutation directe des champs. Les clients reçoivent l'état via les
    /// NetworkMessage de Jobs.Network, pas via SyncVar.
    /// </summary>
    public class MissionInstance {
        public string InstanceId { get; }
        public MissionDefinition Definition { get; }
        public uint OwnerNetId { get; private set; }
        public MissionContext Context { get; }
        public MissionStatus Status { get; private set; }
        public MissionFailureReason FailureReason { get; private set; }
        public int CurrentStepIndex { get; private set; }
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        /// Argent total versé au joueur pour cette mission, accumulé par les
        /// RewardDefinition au moment où MissionCompleted est dispatch. Lu par
        /// MissionServerManager au moment d'envoyer le MissionFinishedMessage —
        /// nécessite que RewardSystem soit abonné AVANT MissionServerManager.
        /// </summary>
        public int MoneyEarned { get; private set; }

        public void AddMoneyEarned(int amount) {
            if (amount > 0) MoneyEarned += amount;
        }

        /// <summary>
        /// Constellation gains accumulated by le trio des RewardKind constellation
        /// (<c>OwnProfessionPointRewardKind</c>, <c>ProfessionPointRewardKind</c>,
        /// <c>BranchPointRewardKind</c>).
        /// Keyed by currency identifier (branch enum string or profession id) ;
        /// the parallel <see cref="ConstellationGainsLabels"/> dictionary keeps
        /// the user-facing label for each entry so the toast can display
        /// "+5 Livreur" instead of "+5 delivery_driver". Same ordering constraint
        /// as MoneyEarned : RewardSystem must be subscribed before MissionServerManager
        /// for these to be populated when MissionFinishedMessage is built.
        /// </summary>
        public Dictionary<string, int> BranchPointsEarned { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> ProfessionPointsEarned { get; } = new Dictionary<string, int>();
        public Dictionary<string, string> ConstellationGainsLabels { get; } = new Dictionary<string, string>();

        public void AddBranchPointsEarned(string branchKey, int amount, string label) {
            if (amount <= 0 || string.IsNullOrEmpty(branchKey)) return;
            BranchPointsEarned.TryGetValue(branchKey, out int cur);
            BranchPointsEarned[branchKey] = cur + amount;
            if (!string.IsNullOrEmpty(label)) ConstellationGainsLabels[branchKey] = label;
        }

        public void AddProfessionPointsEarned(string professionId, int amount, string label) {
            if (amount <= 0 || string.IsNullOrEmpty(professionId)) return;
            ProfessionPointsEarned.TryGetValue(professionId, out int cur);
            ProfessionPointsEarned[professionId] = cur + amount;
            if (!string.IsNullOrEmpty(label)) ConstellationGainsLabels[professionId] = label;
        }

        private readonly List<MissionStepInstance> steps;

        public MissionStepInstance CurrentStep =>
            CurrentStepIndex < steps.Count ? steps[CurrentStepIndex] : null;

        public bool IsAvailable => Status == MissionStatus.Available;
        public bool HasOwner => OwnerNetId != 0u;

        private MissionInstance(MissionDefinition definition, uint ownerNetId, MissionContext context, MissionStatus initialStatus) {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            InstanceId = Guid.NewGuid().ToString("N");
            Definition = definition;
            OwnerNetId = ownerNetId;
            Context = context ?? new MissionContext();
            Status = initialStatus;

            steps = new List<MissionStepInstance>(definition.Steps.Count);
            for (int i = 0; i < definition.Steps.Count; i++) {
                var stepDef = definition.Steps[i];
                if (stepDef == null) continue;
                steps.Add(stepDef.CreateInstance(this));
            }
        }

        public static MissionInstance CreateOffer(MissionDefinition def, uint ownerNetId, MissionContext ctx)
            => new MissionInstance(def, ownerNetId, ctx, MissionStatus.Offered);

        public static MissionInstance CreatePublished(MissionDefinition def, MissionContext ctx)
            => new MissionInstance(def, 0u, ctx, MissionStatus.Available);

        public void Accept() {
            if (Status != MissionStatus.Offered) return;
            Status = MissionStatus.Active;
            ElapsedSeconds = 0f;
            EnterCurrentStep();
            MissionEvents.RaiseMissionAccepted(this);
        }

        public bool Take(uint newOwnerNetId) {
            if (Status != MissionStatus.Available || newOwnerNetId == 0u) return false;
            OwnerNetId = newOwnerNetId;
            Status = MissionStatus.Active;
            ElapsedSeconds = 0f;
            EnterCurrentStep();
            MissionEvents.RaiseMissionTaken(this);
            return true;
        }

        public void Tick(float dt) {
            if (Status == MissionStatus.Available) {
                ElapsedSeconds += dt;
                if (Definition.BoardExpirationSeconds > 0f && ElapsedSeconds > Definition.BoardExpirationSeconds) {
                    FinalizeFail(MissionFailureReason.Expired, MissionStatus.Expired);
                }
                return;
            }

            if (Status != MissionStatus.Active) return;

            ElapsedSeconds += dt;
            if (Definition.ExpirationSeconds > 0f && ElapsedSeconds > Definition.ExpirationSeconds) {
                FinalizeFail(MissionFailureReason.Expired, MissionStatus.Expired);
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
            if (Status != MissionStatus.Active) return;
            CurrentStep?.OnExit();
            CurrentStepIndex++;
            if (CurrentStepIndex >= steps.Count) {
                Complete();
                return;
            }
            EnterCurrentStep();
            MissionEvents.RaiseStepAdvanced(this);
        }

        public void Fail(MissionFailureReason reason)   => FinalizeFail(reason, MissionStatus.Failed);
        public void Abandon()                        => FinalizeFail(MissionFailureReason.Cancelled, MissionStatus.Abandoned);
        public void OwnerDisconnected()              => FinalizeFail(MissionFailureReason.OwnerDisconnected, MissionStatus.Failed);

        private void Complete() {
            if (Status != MissionStatus.Active) return;
            CurrentStep?.OnExit();
            Status = MissionStatus.Completed;
            MissionEvents.RaiseMissionCompleted(this);
        }

        private void FinalizeFail(MissionFailureReason reason, MissionStatus terminalStatus) {
            if (IsTerminal(Status)) return;
            if (Status == MissionStatus.Active) CurrentStep?.OnExit();
            FailureReason = reason;
            Status = terminalStatus;
            MissionEvents.RaiseMissionFailed(this);
        }

        private void EnterCurrentStep() {
            var step = CurrentStep;
            if (step == null) return;
            step.MarkRunning();
            step.OnEnter();
        }

        private static bool IsTerminal(MissionStatus s) =>
            s == MissionStatus.Completed || s == MissionStatus.Failed ||
            s == MissionStatus.Abandoned || s == MissionStatus.Expired;
    }
}
