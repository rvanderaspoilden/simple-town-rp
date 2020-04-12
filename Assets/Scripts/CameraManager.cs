using System;
using UnityEngine;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Only for debug")]
        [SerializeField] private new Camera camera;

        private RaycastHit hit;

        private void Awake() {
            this.camera = GetComponent<Camera>();
        }

        void Update() {
            if (Input.GetMouseButtonDown(0) && Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit)) {
                RoomManager.Instance.MovePlayerTo(hit.point);
            }
        }
    }
}