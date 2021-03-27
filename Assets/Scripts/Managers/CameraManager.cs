using System.Collections.Generic;
using System.Linq;
using AI.States;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using Sim.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim {
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private LayerMask layerMaskInFreeMode;

        private List<PropsRenderer> displayedPropsRenderers;

        private BuildCamera buildCamera;

        private ThirdPersonCamera tpsCamera;

        private float lastCameraPosition;

        private RaycastHit hit;

        private CameraModeEnum currentMode;

        private float mouseClickTimer;

        private new Camera camera;

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
            this.displayedPropsRenderers = new List<PropsRenderer>();

            this.buildCamera.enabled = false;
            this.tpsCamera.enabled = false;

            DontDestroyOnLoad(this.gameObject);
        }

        private void Start() {
            Character.OnStateChanged += OnStateChanged;
        }

        private void OnDestroy() {
            Character.OnStateChanged -= OnStateChanged;
        }

        public Camera Camera => camera;

        void Update() {
            //this.ManageWorldTransparency();

            if (this.currentMode == CameraModeEnum.FREE && !EventSystem.current.IsPointerOverGameObject()) {
                this.ManageInteraction();
            }
        }

        public CameraModeEnum GetMode() {
            return this.currentMode;
        }

        private void OnStateChanged(Character character, StateType state) {
            if (character == RoomManager.LocalCharacter) {
                if (state == StateType.FREE) {
                    this.SetCurrentMode(CameraModeEnum.FREE);
                } else {
                    this.SetCurrentMode(CameraModeEnum.BUILD);
                }
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

            if ((leftMouseClick || rightMouseClick || leftMousePressed) &&
                Physics.Raycast(this.camera.ScreenPointToRay(Input.mousePosition), out hit, 100, this.layerMaskInFreeMode)) {
                Props propsToInteract = hit.collider.GetComponentInParent<Props>();
                Character character = hit.collider.GetComponent<Character>();

                if (propsToInteract) {
                    if (leftMousePressed && propsToInteract.GetType() == typeof(Ground)) {
                        RoomManager.LocalCharacter.MoveTo(hit.point);
                    } else if (!leftMousePressed) {
                        if (propsToInteract.IsInteractable()) {
                            bool canInteract = RoomManager.LocalCharacter.CanInteractWith(propsToInteract, hit.point);
                            Action[] actions = propsToInteract.GetActions();

                            if (leftMouseClick && (RoomManager.LocalCharacter.CurrentState().GetType() == typeof(CharacterMove) ||
                                                   RoomManager.LocalCharacter.CurrentState().GetType() == typeof(CharacterIdle))) {
                                actions = propsToInteract.GetActions(true);

                                canInteract = canInteract || (actions.Length == 1 && actions[0].Type.Equals(ActionTypeEnum.LOOK));
                            }

                            if (canInteract) {
                                if (RoomManager.LocalCharacter.CurrentState().GetType() == typeof(CharacterMove)) {
                                    RoomManager.LocalCharacter.Idle();
                                } else if (RoomManager.LocalCharacter.CurrentState().GetType() == typeof(CharacterIdle)) {
                                    RoomManager.LocalCharacter.LookAt(propsToInteract.transform);
                                }

                                HUDManager.Instance.ShowContextMenu(actions, propsToInteract.transform, leftMouseClick);
                            } else {
                                RoomManager.LocalCharacter.SetTarget(hit.point, propsToInteract, leftMouseClick);
                            }
                        } else if (leftMouseClick) {
                            RoomManager.LocalCharacter.SetTarget(hit.point, propsToInteract);
                        }
                    }
                } else if (rightMouseClick && character && character != RoomManager.LocalCharacter) {
                    if (RoomManager.LocalCharacter.CurrentState().GetType() == typeof(CharacterMove)) {
                        RoomManager.LocalCharacter.Idle();
                    }

                    if (RoomManager.LocalCharacter.CurrentState().GetType() == typeof(CharacterIdle)) {
                        RoomManager.LocalCharacter.LookAt(character.transform);
                        HUDManager.Instance.ShowContextMenu(character.Actions, character.transform);
                    }
                }
            }
        }
    }
}