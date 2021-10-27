using System.Collections.Generic;
using AI.States;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private LayerMask layerMaskInFreeMode;

        private BuildCamera buildCamera;

        private ThirdPersonCamera tpsCamera;

        private float lastCameraPosition;

        private RaycastHit hit;

        private CameraModeEnum currentMode;

        private float mouseClickTimer;

        private new Camera camera;

        private bool startLeftClickValid;

        public static CameraManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }


            this.camera = GetComponentInChildren<Camera>();
            this.buildCamera = GetComponent<BuildCamera>();
            this.tpsCamera = GetComponent<ThirdPersonCamera>();

            this.buildCamera.enabled = false;
            this.tpsCamera.enabled = false;

            DontDestroyOnLoad(this.gameObject);
        }

        private void Start() {
            PlayerController.OnStateChanged += OnStateChanged;
        }

        private void OnDestroy() {
            PlayerController.OnStateChanged -= OnStateChanged;
        }

        public Camera Camera => camera;

        public void SetCameraTarget(Transform target) {
            this.tpsCamera.SetCameraTarget(target);
        }

        void Update() {
            if (PlayerController.Local == null || PlayerController.Local.Died) return;

            if (this.currentMode == CameraModeEnum.FREE) {
                this.ManageInteraction();
            }
        }

        public CameraModeEnum GetMode() {
            return this.currentMode;
        }

        private void OnStateChanged(PlayerController player, StateType state) {
            if (player.isLocalPlayer) {
                if (state == StateType.FREE) {
                    this.SetCurrentMode(CameraModeEnum.FREE);
                } else {
                    this.SetCurrentMode(CameraModeEnum.BUILD);
                }
            }
        }

        private void SetCurrentMode(CameraModeEnum mode) {
            this.currentMode = mode;
            this.buildCamera.enabled = mode == CameraModeEnum.BUILD;
            this.tpsCamera.enabled = mode == CameraModeEnum.FREE;

            if (this.currentMode == CameraModeEnum.BUILD) {
                this.buildCamera.Setup(this.tpsCamera.GetVirtualCamera());
            } else {
                this.tpsCamera.Setup(this.buildCamera.GetVirtualCamera());
            }
        }

        private void ManageInteraction() {
            bool leftMouseClick = Input.GetMouseButtonUp(0);
            bool leftMousePressed = Input.GetMouseButton(0);
            bool rightMouseClick = Input.GetMouseButtonUp(1);

            if (Input.GetMouseButtonDown(0)) {
                this.startLeftClickValid = !EventSystem.current.IsPointerOverGameObject();
            }

            if (((leftMouseClick || leftMousePressed) && !this.startLeftClickValid) || EventSystem.current.IsPointerOverGameObject()) {
                return;
            }

            Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);

            if ((leftMouseClick || rightMouseClick || leftMousePressed) && Physics.Raycast(ray.origin, ray.direction, out hit, 100, this.layerMaskInFreeMode)) {
                Props propsToInteract = hit.collider.GetComponentInParent<Props>();
                Item itemToInteract = hit.collider.GetComponentInParent<Item>();
                PlayerController player = hit.collider.GetComponent<PlayerController>();

                if (propsToInteract && !leftMousePressed) {
                    if (propsToInteract.IsInteractable()) {
                        bool canInteract = PlayerController.Local.CanInteractWith(propsToInteract, hit.point);
                        Action[] actions = propsToInteract.GetActions();

                        if (leftMouseClick && (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove) ||
                                               PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle))) {
                            actions = propsToInteract.GetActions(true);

                            canInteract = canInteract || (actions.Length == 1 && actions[0].Type.Equals(ActionTypeEnum.LOOK));
                        }

                        if (canInteract) {
                            if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove)) {
                                PlayerController.Local.Idle();
                            } else if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle)) {
                                PlayerController.Local.LookAt(propsToInteract.transform);
                            }

                            HUDManager.Instance.ShowContextMenu(actions, propsToInteract.transform, leftMouseClick);
                        } else {
                            PlayerController.Local.SetTarget(hit.point, propsToInteract, leftMouseClick);
                        }
                    } else {
                        PlayerController.Local.SetTarget(hit.point, propsToInteract);
                    }
                } else if (itemToInteract && !itemToInteract.HasOwner() && rightMouseClick) {
                    bool canInteract = PlayerController.Local.CanInteractWith(itemToInteract, hit.point);
                    Action[] actions = itemToInteract.GetActions();

                    if (canInteract) {
                        if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove)) {
                            PlayerController.Local.Idle();
                        } else if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle)) {
                            PlayerController.Local.LookAt(itemToInteract.transform);
                        }

                        HUDManager.Instance.ShowContextMenu(actions, itemToInteract.transform, leftMouseClick);
                    }
                } else if (!propsToInteract && leftMousePressed && hit.collider.gameObject.layer.Equals(LayerMask.NameToLayer("Ground"))) {
                    PlayerController.Local.MoveTo(hit.point);
                } else if (rightMouseClick && player) {
                    if (player == PlayerController.Local) {
                        HUDManager.Instance.ToggleInventory();
                    } else {
                        if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove)) {
                            PlayerController.Local.Idle();
                        }

                        if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle)) {
                            PlayerController.Local.LookAt(player.transform);
                            HUDManager.Instance.ShowContextMenu(player.Actions, player.transform);
                        }
                    }
                }
            }
        }
    }
}