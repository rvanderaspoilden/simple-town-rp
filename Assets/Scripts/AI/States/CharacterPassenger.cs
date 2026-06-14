using DG.Tweening;
using Sim;
using UnityEngine;

namespace AI.States {
    /// <summary>
    /// État local d'un PASSAGER de véhicule. Calqué sur <see cref="CharacterDrive"/> mais sans
    /// conduite : le joueur est assis (pose + caméra véhicule), ne contrôle pas le véhicule, et
    /// descend avec la touche X.
    ///
    /// Le PARENTAGE sous le siège passager est géré par <see cref="VehicleController"/> sur tous
    /// les clients (hook SyncVar) ; cet état ne parente rien. Il ne tourne que pour le passager
    /// LOCAL : pose, caméra, flag IsPassenger, lecture de la touche de sortie.
    /// </summary>
    public class CharacterPassenger : IState {
        private readonly PlayerController player;
        private readonly VehicleController vehicle;

        public CharacterPassenger(PlayerController player, VehicleController vehicle) {
            this.player = player;
            this.vehicle = vehicle;
        }

        public void OnEnter() {
            this.player.NavMeshAgent.enabled = false;
            this.player.Collider.enabled = false;
            this.player.PlayerState = PlayerState.SITTING;
            this.player.IsPassenger = true;

            this.player.transform.DOComplete();

            this.player.SetAnimatorAction(CharacterAnimatorAction.SIT);
            this.player.SetHeadTargetPosition(this.player.SitHeadPosition);

            // Vue embarquée : on suit le véhicule (même caméra dédiée que le conducteur).
            CameraManager.Instance.EnterVehicleCamera(this.vehicle);

            // HUD : vie du véhicule + rappel touche de sortie (mode passager).
            HUDManager.Instance.ShowVehicleHud(this.vehicle, false);
        }

        public void Tick() {
            // Sortie passager : touche X (le passager n'est pas owner du véhicule, donc
            // VehicleController.Update ne lit pas son input — on le fait ici).
            if (Input.GetKeyDown(KeyCode.X) && this.vehicle != null) {
                this.vehicle.RequestPassengerExit();
            }
        }

        public void OnExit() {
            this.player.IsPassenger = false;
            HUDManager.Instance.HideVehicleHud();
            this.player.SetAnimatorAction(CharacterAnimatorAction.NONE);
            this.player.SetHeadTargetPosition(this.player.IdleHeadPosition);

            Vector3 exit = this.vehicle != null ? this.vehicle.GetExitPosition()
                                                : this.player.transform.position;
            this.player.transform.position = exit;
            this.player.transform.rotation = Quaternion.identity;

            this.player.Collider.enabled = true;
            this.player.NavMeshAgent.enabled = true;

            CameraManager.Instance.ExitVehicleCamera();
            CameraManager.Instance.SetCameraTarget(this.player.GetHeadTargetForCamera());
        }
    }
}
