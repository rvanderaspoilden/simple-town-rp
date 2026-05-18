using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using Sim.SubGames.Packaging;

namespace Sim.Jobs {
    /// <summary>
    /// Runtime du UseMachineStep. S'enregistre dans un registre statique
    /// indexé par playerNetId à l'entrée pour que le handler du message
    /// JobUseMachineMessage puisse le retrouver. Le spawn dans les mains
    /// + l'avance du step se font à l'appel de OnMachineUsed.
    /// </summary>
    public sealed class UseMachineStepInstance : JobStepInstance {
        public const string CtxEntityIdKey = "packageEntityId";
        public const string CtxRoomIdKey   = "packageRoomId";
        public const string CtxScoreKey    = "packageScore";
        public const string CtxRatingKey   = "packageRating";

        private static readonly Dictionary<uint, UseMachineStepInstance> _waiting =
            new Dictionary<uint, UseMachineStepInstance>();

        private readonly UseMachineStepDefinition def;
        private int _spawnedEntityId = -1;

        public UseMachineStepInstance(JobInstance job, UseMachineStepDefinition definition) : base(job) {
            def = definition;
        }

        public override void OnEnter() {
            if (def.ItemConfig == null || def.ItemConfig.ID <= 0) {
                GameLogger.System.Error("UseMachineStep_InvalidItemConfig {JobId}", job.Definition.JobId);
                Fail(JobFailureReason.None);
                return;
            }

            // Référence le step pour ce joueur — un seul UseMachineStep actif
            // simultanément par joueur (garanti par MaxConcurrentPerPlayer=1).
            _waiting[job.OwnerNetId] = this;
        }

        public override void Tick(float dt) {
            var owner = JobTargetRegistry.Instance.GetPlayer(job.OwnerNetId);
            if (owner == null || !owner.IsAvailable) {
                Fail(JobFailureReason.OwnerDisconnected);
                return;
            }
        }

        public override void OnExit() {
            // Libère le slot d'attente.
            if (_waiting.TryGetValue(job.OwnerNetId, out var waiting) && waiting == this) {
                _waiting.Remove(job.OwnerNetId);
            }

            // Si on échoue avant le pickup et qu'un item a quand même été
            // spawné (corner case), on le nettoie.
            if (Status != StepStatus.Succeeded && _spawnedEntityId >= 0) {
                ServerItemManager.Instance.DespawnItem(def.RoomId, _spawnedEntityId);
                _spawnedEntityId = -1;
            }
        }

        /// <summary>
        /// Appelé par JobSystemBootstrap quand le client envoie JobUseMachineMessage.
        /// Si le step a une PackagingSubGameConfig attachée, le serveur recalcule
        /// le score à partir du snapshot envoyé par le client (anti-triche). Le
        /// score sert pour les logs / futurs rewards modulés ; le spawn reste
        /// indépendant pour rester cozy (jamais bloqué).
        /// </summary>
        public static void TryUseMachineFor(NetworkConnectionToClient conn, string machineId,
                                            PackagePlacementSnapshot snapshot) {
            if (conn == null || conn.identity == null) return;
            uint netId = conn.identity.netId;

            if (!_waiting.TryGetValue(netId, out var step)) {
                GameLogger.System.Debug("UseMachine_NoActiveStep {NetId} {MachineId}", netId, machineId);
                conn.Send(new JobNotificationMessage {
                    text = "Aucune mission ne te demande d'utiliser cette machine."
                });
                return;
            }

            // Validation serveur autoritaire : on rejoue le placement contre
            // NOTRE PackageOrderDefinition (jamais celle envoyée par le client).
            var cfg = step.def.PackagingConfig;
            if (cfg != null && cfg.order != null) {
                var serverScore = PackageScoringSystem.EvaluateFromSnapshot(snapshot, cfg.order, cfg);
                GameLogger.System.Info(
                    "PackagingScore_Server {NetId} {JobId} {Total} {Rating} {Space} {FragileOk} {HeavyOk} {AllPlaced}",
                    netId, step.job.Definition.JobId, serverScore.total, serverScore.rating,
                    serverScore.spaceRatio, serverScore.fragileOk, serverScore.heavyOk,
                    serverScore.allItemsPlaced);
                step.job.Context.Set(CtxScoreKey,  serverScore.total);
                step.job.Context.Set(CtxRatingKey, (int)serverScore.rating);
            }

            step.HandleMachineUsed(conn);
        }

        private void HandleMachineUsed(NetworkConnectionToClient conn) {
            // Vérifie qu'on n'a pas déjà spawné (anti double-clic).
            if (_spawnedEntityId >= 0) {
                conn?.Send(new JobNotificationMessage {
                    text = "Tu as déjà ton colis."
                });
                return;
            }

            int entityId = ServerItemManager.Instance.SpawnItemInHand(
                def.RoomId, def.ItemConfig.ID,
                FindOwnerConn(), def.ItemConfig, persistent: false);

            if (entityId < 0) {
                GameLogger.System.Warning("UseMachine_NoFreeHand {NetId} {JobId}",
                    job.OwnerNetId, job.Definition.JobId);
                conn?.Send(new JobNotificationMessage {
                    text = "Tes mains sont pleines."
                });
                return;
            }

            _spawnedEntityId = entityId;
            ServerItemManager.Instance.SetAuthorizedHolder(def.RoomId, entityId, job.OwnerNetId);

            job.Context.Set(CtxEntityIdKey, entityId);
            job.Context.Set(CtxRoomIdKey,   def.RoomId);

            GameLogger.System.Info("UseMachineStep_Spawned {EntityId} {NetId} {JobId}",
                entityId, job.OwnerNetId, job.Definition.JobId);

            Succeed();
        }

        private NetworkConnectionToClient FindOwnerConn() {
            if (!NetworkServer.spawned.TryGetValue(job.OwnerNetId, out var identity)) return null;
            return identity != null ? identity.connectionToClient : null;
        }
    }
}
