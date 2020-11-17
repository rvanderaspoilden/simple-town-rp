using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using Photon.Pun;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using Sim.Scriptables;
using Sim.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private CinemachineFreeLook freelookCamera;

        [SerializeField]
        private VirtualCameraFollow virtualCameraFollow;

        [SerializeField]
        private LayerMask layerMaskInFreeMode;

        [Header("Only for debug")]
        [SerializeField]
        private new Camera camera;

        [SerializeField]
        private CameraModeEnum currentMode;

        [SerializeField]
        private List<PropsRenderer> displayedPropsRenderers;

        private float lastCameraPosition;

        private RaycastHit hit;

        private bool forceWallHidden;

        public static CameraManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;

            this.camera = GetComponentInChildren<Camera>();
            this.displayedPropsRenderers = new List<PropsRenderer>();
        }

        private void Start() {
            BuildPreviewPanelUI.OnToggleHideProps += TogglePropsVisible;
            BuildPreviewPanelUI.OnToggleHideWalls += ToggleWallVisible;

            Player.OnStateChanged += OnStateChanged;
        }

        private void OnDestroy() {
            BuildPreviewPanelUI.OnToggleHideProps -= TogglePropsVisible;
            BuildPreviewPanelUI.OnToggleHideWalls -= ToggleWallVisible;

            Player.OnStateChanged -= OnStateChanged;
        }

        void Update() {
            //this.ManageWorldTransparency();

            if (Input.GetKeyDown(KeyCode.H) && this.currentMode == CameraModeEnum.FREE) {
                this.ToggleWallVisible(!this.forceWallHidden);
            }

            if (this.currentMode == CameraModeEnum.FREE && !EventSystem.current.IsPointerOverGameObject()) {
                this.ManageInteraction();
            }

            if (Input.GetMouseButtonDown(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 300f;
            }

            if (Input.GetMouseButtonUp(1)) {
                this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
            }
        }

        public CameraModeEnum GetCurrentMode() {
            return this.currentMode;
        }

        /*public void ManageWorldTransparency() {
            if (!RoomManager.LocalPlayer || this.currentOpenedBucket || (this.currentPropSelected && this.currentPropSelected.IsWallProps())) {
                return;
            }

            this.lastCameraPosition = this.camera.transform.position.magnitude;

            Vector3 dir = -(this.camera.transform.position -
                            (this.currentPropSelected ? this.currentPropSelected.transform.position : RoomManager.LocalPlayer.transform.position));
            Debug.DrawRay(this.camera.transform.position, dir, Color.blue);

            RaycastHit[] hits = Physics.RaycastAll(this.camera.transform.position, dir, 20, (1 << 10 | 1 << 12));
            if (hits.Length > 0) {
                List<PropsRenderer> hiddenObject = new List<PropsRenderer>();

                foreach (RaycastHit wallHit in hits) {
                    hiddenObject.AddRange(this.HidePropsNear(wallHit.point));
                }

                this.ResetRendererForPropsNotIn(hiddenObject);
            } else if (this.displayedPropsRenderers.Count > 0) // If camera doesn't hit wall so reset all hidden props
            {
                this.displayedPropsRenderers.ForEach(propsRenderer => propsRenderer.SetState(VisibilityStateEnum.SHOW));
                this.displayedPropsRenderers.Clear();
            }
        }*/

        private void TogglePropsVisible(bool hide) {
            FindObjectsOfType<Props>().ToList().Where(x => x.GetType() != typeof(Wall)).Select(x => x.GetComponent<PropsRenderer>()).ToList().ForEach(
                propsRenderer => {
                    if (propsRenderer && propsRenderer.IsHideable()) {
                        propsRenderer.SetVisibilityMode(hide ? VisibilityModeEnum.FORCE_HIDE : VisibilityModeEnum.AUTO);
                    }
                });
        }

        private void ToggleWallVisible(bool hide) {
            this.forceWallHidden = hide;

            FindObjectsOfType<Wall>().ToList().Where(x => !x.IsExteriorWall()).Select(x => x.GetComponent<PropsRenderer>()).ToList().ForEach(propsRenderer => {
                if (propsRenderer) {
                    propsRenderer.SetVisibilityMode(this.forceWallHidden ? VisibilityModeEnum.FORCE_HIDE : VisibilityModeEnum.AUTO);
                }
            });
        }

        private void OnStateChanged(StateType state) {
            if (state == StateType.FREE) {
                this.currentMode = CameraModeEnum.FREE;
            } else {
                this.currentMode = CameraModeEnum.BUILD;
            }
        }

        private List<PropsRenderer> HidePropsNear(Vector3 pos) {
            List<PropsRenderer> objectsToHide = Physics.OverlapSphere(pos, 3f)
                .ToList()
                .Select(x => x.GetComponentInParent<PropsRenderer>())
                .ToList();

            objectsToHide.ForEach(propsRenderer => {
                if (propsRenderer && propsRenderer.CanInteractWithCameraDistance()) {
                    // prevent NPE due to get componentInParent
                    propsRenderer.SetState(VisibilityStateEnum.HIDE);

                    this.displayedPropsRenderers.Add(propsRenderer);
                }
            });

            return objectsToHide;
        }

        private void ResetRendererForPropsNotIn(List<PropsRenderer> objectHidden) {
            // Reset objects which aren't in view area
            IEnumerable<PropsRenderer> difference = this.displayedPropsRenderers.Except(objectHidden);

            foreach (PropsRenderer propsRenderer in difference) {
                propsRenderer.SetState(VisibilityStateEnum.SHOW);
            }
        }

        private void SwitchToFreeMode() {
            this.currentMode = CameraModeEnum.FREE;
            this.virtualCameraFollow.SetTarget(RoomManager.LocalPlayer.GetHeadTargetForCamera());
            this.freelookCamera.enabled = true;
            this.freelookCamera.m_XAxis.m_MaxSpeed = 0f;
        }

        private void ManageInteraction() {
            if (Input.GetMouseButtonDown(0) && Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, this.layerMaskInFreeMode)) {
                Props objectToInteract = hit.collider.GetComponentInParent<Props>();
                bool canInteract = objectToInteract && RoomManager.LocalPlayer && RoomManager.LocalPlayer.CanInteractWith(objectToInteract);

                if (canInteract) {
                    HUDManager.Instance.DisplayContextMenu(true, Input.mousePosition, objectToInteract);
                } else {
                    RoomManager.Instance.MovePlayerTo(hit.point);
                    HUDManager.Instance.DisplayContextMenu(false, Vector3.zero);
                }
            }
        }

        private void ManageCameraMovement() {
            /*if (Input.GetMouseButton(1)) {
                // Rotation
                this.transform.Rotate(new Vector3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0) * this.cameraRotationSpeed);
                this.transform.eulerAngles = new Vector3(this.transform.rotation.eulerAngles.x, this.transform.rotation.eulerAngles.y, 0);
            }

            if (Input.GetMouseButton(2)) {
                // Movement
                this.transform.Translate(new Vector3(-Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y"), 0) * this.cameraMoveSpeed);
            }

            float mouseScrollValue = Input.GetAxis("Mouse ScrollWheel");

            if (mouseScrollValue != 0f) {
                // Zoom
                this.transform.Translate(Vector3.forward * mouseScrollValue * this.cameraZoomSpeed);
            }*/
        }

        public void SetCameraTarget(Transform transform) {
            this.virtualCameraFollow.SetTarget(transform);
        }
    }
}