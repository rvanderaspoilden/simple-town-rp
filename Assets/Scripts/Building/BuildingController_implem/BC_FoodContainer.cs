using UnityEngine;

namespace Sim.Building.BuildingController_implem {
    public class BC_FoodContainer : BuildingController {
        [SerializeField]
        private Transform playerPositionPoint;

        [SerializeField]
        private Transform cameraPositionPoint;

        public override void OnStartAuthority() {
            base.OnStartAuthority();
            
            // Move it in character state
            PlayerController playerController = PlayerController.Local;
            playerController.NavMeshAgent.enabled = false;
            playerController.Collider.enabled = false;
            playerController.transform.position = playerPositionPoint.position;
            playerController.transform.rotation = playerPositionPoint.rotation;
            
            CameraManager.Instance.SetFpsCamera(cameraPositionPoint);
        }
    }
}