using AI.States;
using Interaction;
using Sim.Enums;
using Sim.Interactables;
using Sim.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private LayerMask layerMaskInFreeMode;

        [SerializeField]
        private LayerMask defaultCullingMask;
        
        [SerializeField]
        private LayerMask fpsCullingMask;
        
        [SerializeField]
        private GameObject fpsCamera;

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
            this.fpsCamera.SetActive(false);

            DontDestroyOnLoad(this.gameObject);
        }

        private void Start() {
            PlayerController.OnStateChanged += OnStateChanged;
        }

        private void OnDestroy() {
            PlayerController.OnStateChanged -= OnStateChanged;
        }

        public Camera Camera => camera;

        public UniversalAdditionalCameraData GetCameraData() {
            return this.camera.GetUniversalAdditionalCameraData();
        }

        public void SetCameraTarget(Transform target) {
            this.tpsCamera.SetCameraTarget(target);
        }

        void Update() {
            if (PlayerController.Local == null || PlayerController.Local.PlayerState == PlayerState.DIED) return;

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

        public void SetFpsCamera(Transform point) {
            this.fpsCamera.transform.position = point.position;
            this.fpsCamera.transform.rotation = point.rotation;
            this.SetCurrentMode(CameraModeEnum.FPS);
        }

        public void SetCurrentMode(CameraModeEnum mode) {
            this.currentMode = mode;
            this.buildCamera.enabled = mode == CameraModeEnum.BUILD;
            this.tpsCamera.enabled = mode == CameraModeEnum.FREE;
            this.fpsCamera.SetActive(mode == CameraModeEnum.FPS);

            if (this.currentMode == CameraModeEnum.BUILD) {
                this.buildCamera.Setup(this.tpsCamera.GetVirtualCamera());
                this.camera.cullingMask = this.defaultCullingMask;
            } else if (this.currentMode == CameraModeEnum.FREE) {
                this.tpsCamera.Setup(this.buildCamera.GetVirtualCamera());
                this.camera.cullingMask = this.defaultCullingMask;
            } else {
                this.camera.cullingMask = this.fpsCullingMask;
            }
        }

        private void ManageInteraction() {
            if (SubGameController.IsActive) return;

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

            if (!(leftMouseClick || rightMouseClick || leftMousePressed)) return;

            // Ignore trigger colliders: props can host triggers for occupancy detection (e.g. DoorPropSource)
            // and we don't want those to intercept the click — only solid geometry should be hit.
            RaycastHit[] hits = Physics.RaycastAll(ray.origin, ray.direction, 100, this.layerMaskInFreeMode, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            bool useLeftClickFilter = leftMouseClick || leftMousePressed;
            bool hasHit = false;
            foreach (RaycastHit h in hits) {
                if (useLeftClickFilter) {
                    IInteractable ii = h.collider.GetComponentInParent<IInteractable>();
                    if (ii != null && ii.IsRightClickOnly()) continue;
                }
                hit = h;
                hasHit = true;
                break;
            }
            if (!hasHit) return;

            {
                IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
                PlayerController player = hit.collider.GetComponent<PlayerController>();

                if (interactable != null && !leftMousePressed) {
                    if (interactable.IsInteractable()) {
                        bool canInteract = PlayerController.Local.CanInteractWith(interactable, hit.point);
                        Action[] actions = interactable.GetActions();

                        if (leftMouseClick && (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove) ||
                                               PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle))) {
                            actions = interactable.GetActions(true);

                            canInteract = canInteract || (actions.Length == 1 && actions[0].Type.Equals(ActionTypeEnum.LOOK));
                        }

                        if (canInteract) {
                            if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove)) {
                                PlayerController.Local.Idle();
                            } else if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle)) {
                                PlayerController.Local.LookAt(interactable.transform);
                            }

                            HUDManager.Instance.ShowContextMenu(actions, interactable.transform, leftMouseClick);
                        } else {
                            PlayerController.Local.SetTarget(hit.point, interactable, leftMouseClick);
                        }
                    } else {
                        PlayerController.Local.SetTarget(hit.point, interactable);
                    }
                } else if (interactable == null && leftMousePressed && hit.collider.gameObject.layer.Equals(LayerMask.NameToLayer("Ground"))) {
                    PlayerController.Local.MoveTo(hit.point);
                } else if (rightMouseClick && player) {
                    if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterMove) ||
                        PlayerController.Local.CurrentState().GetType() == typeof(CharacterInteract)) {
                        PlayerController.Local.Idle();
                    }

                    if (player == PlayerController.Local) {
                        HUDManager.Instance.CloseContextMenu();
                        HUDManager.Instance.ToggleInventory();
                    } else if (PlayerController.Local.CurrentState().GetType() == typeof(CharacterIdle)) {
                        PlayerController.Local.LookAt(player.transform);
                        HUDManager.Instance.ShowContextMenu(player.Actions, player.transform);
                    }
                }
            }
        }
    }
}