using DG.Tweening;
using Sim;
using UnityEngine;
using UnityEngine.AI;

namespace AI.States {
    /// <summary>
    /// État local du conducteur d'un véhicule. Calqué sur <see cref="CharacterSit"/> :
    /// désactive le NavMeshAgent + le collider, joue la pose assise et bascule la caméra
    /// sur l'ancre caméra du véhicule.
    ///
    /// IMPORTANT — répartition des responsabilités :
    ///   - Le PARENTAGE du joueur sous le siège (ride-along visuel) est géré par
    ///     <see cref="VehicleController.OnDriverChanged"/> sur TOUS les clients (le joueur
    ///     distant ne joue pas cet état). Cet état ne parente donc rien.
    ///   - Cet état ne tourne que pour le conducteur LOCAL : pose, caméra, flag IsDriving,
    ///     puis repositionnement à la sortie.
    ///
    /// L'input de conduite (WASD) est lu par <see cref="VehicleController"/> côté owner.
    /// </summary>
    public class CharacterDrive : IState {
        private readonly PlayerController player;
        private readonly VehicleController vehicle;

        public CharacterDrive(PlayerController player, VehicleController vehicle) {
            this.player = player;
            this.vehicle = vehicle;
        }

        public void OnEnter() {
            this.player.NavMeshAgent.enabled = false;
            this.player.Collider.enabled = false;
            this.player.PlayerState = PlayerState.SITTING;
            this.player.IsDriving = true;
            this.player.CurrentVehicle = this.vehicle;

            this.player.transform.DOComplete(); // stoppe un éventuel tween LookAt

            this.player.SetAnimatorAction(CharacterAnimatorAction.SIT);
            this.player.SetHeadTargetPosition(this.player.SitHeadPosition);

            // Caméra Cinemachine dédiée à la conduite (centrée sur le véhicule, rotation souris seule).
            CameraManager.Instance.EnterVehicleCamera(this.vehicle);

            // HUD de conduite : vitesse km/h + vie + rappel des touches (mode conducteur).
            HUDManager.Instance.ShowVehicleHud(this.vehicle, true);
        }

        public void Tick() { }

        public void OnExit() {
            this.player.IsDriving = false;
            this.player.CurrentVehicle = null;
            this.player.SetAnimatorAction(CharacterAnimatorAction.NONE);
            this.player.SetHeadTargetPosition(this.player.IdleHeadPosition);

            // Repositionne le joueur au point de descente (échantillonné sur le NavMesh).
            Vector3 exit = this.vehicle != null ? this.vehicle.GetExitPosition()
                                                : this.player.transform.position;
            this.player.transform.position = exit;
            this.player.transform.rotation = Quaternion.identity;

            this.player.Collider.enabled = true;
            this.player.NavMeshAgent.enabled = true;

            // Masque le HUD de conduite.
            HUDManager.Instance.HideVehicleHud();

            // Rend la main à la caméra "à pied" (FREE), recalée sur la tête du joueur.
            CameraManager.Instance.ExitVehicleCamera();
            CameraManager.Instance.SetCameraTarget(this.player.GetHeadTargetForCamera());
        }
    }
}
