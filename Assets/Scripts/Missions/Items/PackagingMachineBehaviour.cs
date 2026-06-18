using Mirror;
using Sim.Enums;
using Sim.SubGames.Packaging;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Missions {
    /// <summary>
    /// Machine à emballer / poste de travail à poser dans la scène. Hérite de
    /// CareerInteractableBase pour le gate métier (toast si pas le bon job).
    /// À l'exécution de l'action USE/OPEN côté client, lance le mini-jeu
    /// d'emballage (PackagingSubGame). Une fois le colis validé, envoie
    /// MissionUseMachineMessage au serveur ; le serveur route vers le
    /// UseMachineStep actif du joueur et fait spawn le colis dans les mains.
    /// </summary>
    public class PackagingMachineBehaviour : CareerInteractableBase {
        [Header("Identification")]
        [Tooltip("Id stable de la machine (envoyé au serveur pour logs / futurs filtres).")]
        [SerializeField] private string machineId;

        [Header("Packaging mini-game")]
        [Tooltip("Config du mini-jeu d'emballage à lancer. Si null, la machine envoie directement MissionUseMachineMessage (legacy).")]
        [SerializeField] private PackagingSubGameConfig packagingConfig;

        private bool _miniGameInFlight;

        public string MachineId => machineId;
        public override MissionHighlightKind HighlightKind => MissionHighlightKind.PackagingMachine;
        protected override string GetHighlightId() => machineId;

        protected override void HandleAction(Action action) {
            if (action.Type != ActionTypeEnum.USE && action.Type != ActionTypeEnum.OPEN) return;
            if (!NetworkClient.isConnected) return;
            if (_miniGameInFlight) return;

            if (!HasActiveUseMachineStep()) {
                if (NotificationManager.Instance != null) {
                    NotificationManager.Instance.AddNotification(
                        "Aucune mission ne te demande d'utiliser cette machine.",
                        PhoneAppIds.Career);
                }
                return;
            }

            if (packagingConfig == null || SubGameController.Instance == null) {
                // Legacy direct flow.
                NetworkClient.Send(new MissionUseMachineMessage { machineId = machineId ?? string.Empty });
                return;
            }

            _miniGameInFlight = true;
            PackagingSubGameManager.PendingConfig = packagingConfig;
            PackagingSubGameManager.OnPackageValidated += HandlePackageValidated;
            SubGameController.Instance.LaunchSubGame(packagingConfig, true);
        }

        private void HandlePackageValidated(PackagePlacementSnapshot? snapshot) {
            PackagingSubGameManager.OnPackageValidated -= HandlePackageValidated;
            PackagingSubGameManager.PendingConfig = null;
            _miniGameInFlight = false;

            if (!snapshot.HasValue) {
                // Joueur a annulé — pas de spawn.
                return;
            }
            NetworkClient.Send(new MissionUseMachineMessage {
                machineId = machineId ?? string.Empty,
                snapshot  = snapshot.Value
            });
        }

        /// <summary>
        /// Vérifie que le joueur local a au moins une mission active dont le step
        /// courant est un UseMachineStepDefinition. Sans ça, le serveur rejetterait
        /// l'action mais le client aurait déjà lancé le mini-jeu pour rien.
        /// </summary>
        private static bool HasActiveUseMachineStep() {
            var states = MissionClientManager.Instance?.States;
            if (states == null) return false;
            foreach (var state in states.Values) {
                if (state.Status != MissionStatus.Active) continue;
                var def = state.Definition;
                if (def == null) continue;
                int idx = state.CurrentStepIndex;
                if (idx < 0 || idx >= def.Steps.Count) continue;
                if (def.Steps[idx] is UseMachineStepDefinition) return true;
            }
            return false;
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            PackagingSubGameManager.OnPackageValidated -= HandlePackageValidated;
        }
    }
}
