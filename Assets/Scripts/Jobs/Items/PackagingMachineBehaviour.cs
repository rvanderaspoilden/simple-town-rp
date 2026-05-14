using Mirror;
using Sim.Enums;
using Sim.SubGames.Packaging;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Jobs {
    /// <summary>
    /// Machine à emballer / poste de travail à poser dans la scène. Hérite de
    /// CareerInteractableBase pour le gate métier (toast si pas le bon job).
    /// À l'exécution de l'action USE/OPEN côté client, lance le mini-jeu
    /// d'emballage (PackagingSubGame). Une fois le colis validé, envoie
    /// JobUseMachineMessage au serveur ; le serveur route vers le
    /// UseMachineStep actif du joueur et fait spawn le colis dans les mains.
    /// </summary>
    public class PackagingMachineBehaviour : CareerInteractableBase {
        [Header("Identification")]
        [Tooltip("Id stable de la machine (envoyé au serveur pour logs / futurs filtres).")]
        [SerializeField] private string machineId;

        [Header("Packaging mini-game")]
        [Tooltip("Config du mini-jeu d'emballage à lancer. Si null, la machine envoie directement JobUseMachineMessage (legacy).")]
        [SerializeField] private PackagingSubGameConfig packagingConfig;

        private bool _miniGameInFlight;

        public string MachineId => machineId;

        protected override void HandleAction(Action action) {
            if (action.Type != ActionTypeEnum.USE && action.Type != ActionTypeEnum.OPEN) return;
            if (!NetworkClient.isConnected) return;
            if (_miniGameInFlight) return;

            if (packagingConfig == null || SubGameController.Instance == null) {
                // Legacy direct flow.
                NetworkClient.Send(new JobUseMachineMessage { machineId = machineId ?? string.Empty });
                return;
            }

            _miniGameInFlight = true;
            PackagingSubGameManager.PendingConfig = packagingConfig;
            PackagingSubGameManager.OnPackageValidated += HandlePackageValidated;
            SubGameController.Instance.LaunchSubGame(packagingConfig, true);
        }

        private void HandlePackageValidated(PackageScore? score) {
            PackagingSubGameManager.OnPackageValidated -= HandlePackageValidated;
            PackagingSubGameManager.PendingConfig = null;
            _miniGameInFlight = false;

            if (!score.HasValue) {
                // Joueur a annulé — pas de spawn.
                return;
            }
            NetworkClient.Send(new JobUseMachineMessage { machineId = machineId ?? string.Empty });
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            PackagingSubGameManager.OnPackageValidated -= HandlePackageValidated;
        }
    }
}
