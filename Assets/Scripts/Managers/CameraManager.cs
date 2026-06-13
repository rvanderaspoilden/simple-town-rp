using AI.States;
using Interaction;
using Sim.Entities;
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

        [Header("Run on double click")]
        [Tooltip("Fenêtre temporelle entre deux mouseDown gauche pour qu'ils soient considérés comme un double clic et déclenchent la course.")]
        [SerializeField] private float doubleClickWindow = 0.3f;

        [Header("Curseur magnétique")]
        [Tooltip("Si le curseur ne touche pas pile un item/prop, on cible automatiquement l'interactable à portée le plus proche du curseur à l'écran.")]
        [SerializeField] private bool magneticTargetingEnabled = true;
        [Tooltip("Rayon (m) autour du joueur dans lequel chercher les interactables candidats (≈ portée d'interaction max).")]
        [SerializeField] private float magneticCandidateRadius = 3.5f;
        [Tooltip("Tolérance écran (px, à la résolution de référence) : un candidat n'est aimanté que si sa projection est à moins de cette distance du curseur.")]
        [SerializeField] private float magneticScreenTolerancePixels = 70f;
        [Tooltip("Hauteur d'écran de référence pour la tolérance (px). La tolérance réelle est mise à l'échelle par Screen.height / cette valeur.")]
        [SerializeField] private float magneticToleranceReferenceHeight = 1080f;

        private float _lastLeftDownTime = -10f;
        private bool _pendingRunRequest;

        private BuildCamera buildCamera;

        private ThirdPersonCamera tpsCamera;

        private DriveCamera driveCamera;

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
            this.driveCamera = GetComponent<DriveCamera>();

            this.buildCamera.enabled = false;
            this.tpsCamera.enabled = false;
            if (this.driveCamera != null) this.driveCamera.enabled = false;
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

            // Pendant la conduite, l'input est piloté par le VehicleController (WASD).
            // On neutralise le click-to-move / menu radial pour éviter qu'un clic au sol
            // déclenche un MoveTo qui se battrait avec la conduite.
            if (PlayerController.Local.IsDriving || PlayerController.Local.IsPassenger) {
                if (this._hoveredOutline != null) {
                    this._hoveredOutline.Hide();
                    this._hoveredOutline = null;
                }
                HoverNameTooltip.Hide();
                return;
            }

            if (this.currentMode == CameraModeEnum.FREE) {
                this.ManageInteraction();
                this.ManageHover();
            } else {
                if (this._hoveredOutline != null) {
                    this._hoveredOutline.Hide();
                    this._hoveredOutline = null;
                }
                HoverNameTooltip.Hide();
            }
        }

        private HoverOutline _hoveredOutline;
        private int _outlineLayerBit = -1;
        private int _missionLayerBit = -1;

        /// <summary>
        /// Interaction raycast mask, plus the "Outline" and "MissionHighlight" layer bits.
        /// While a prop is highlighted, its renderers sit on those layers, so we must
        /// keep them hittable or the interaction would flicker.
        /// </summary>
        private LayerMask InteractionMask {
            get {
                if (_outlineLayerBit < 0) {
                    int l = LayerMask.NameToLayer(HoverOutline.OutlineLayerName);
                    _outlineLayerBit = l >= 0 ? (1 << l) : 0;
                }
                if (_missionLayerBit < 0) {
                    int l = LayerMask.NameToLayer("MissionHighlight");
                    _missionLayerBit = l >= 0 ? (1 << l) : 0;
                }
                return this.layerMaskInFreeMode | _outlineLayerBit | _missionLayerBit;
            }
        }

        private static bool WithinPlanarRange(Vector3 a, Vector3 b, float range) {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)) <= range;
        }

        /// <summary>Vrai si le curseur vise exactement (raycast) un joueur distant — sert à
        /// protéger l'interaction sociale (clic droit) du magnétisme items/props.</summary>
        private bool IsCursorOverRemotePlayer() {
            Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit h, 100, this.InteractionMask, QueryTriggerInteraction.Ignore)) {
                PlayerController pc = h.collider.GetComponentInParent<PlayerController>();
                return pc != null && !pc.isLocalPlayer;
            }
            return false;
        }

        /// <summary>
        /// Résout l'interactable visé par le curseur, de façon TOLÉRANTE. D'abord un raycast
        /// exact (visée précise + visée d'objets hors portée pour s'en approcher conservées) ;
        /// si rien d'interactable n'est touché, on aimante sur l'interactable À PORTÉE dont la
        /// projection écran est la plus proche du curseur (dans une petite tolérance en pixels).
        /// Utilisé par le survol ET le clic → l'objet surligné est toujours celui qu'on interagit.
        /// La forgiveness ne concerne QUE la précision du curseur ; la portée/ligne de vue reste
        /// vérifiée par CanInteractWith (et revalidée serveur).
        /// </summary>
        private bool TryResolveInteractable(out IInteractable interactable, out Collider col, out Vector3 point) {
            interactable = null; col = null; point = Vector3.zero;

            // 1. Raycast exact d'abord.
            Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit h, 100, this.InteractionMask, QueryTriggerInteraction.Ignore)) {
                IInteractable exact = h.collider.GetComponentInParent<IInteractable>();
                if (exact.IsAlive() && exact.IsInteractable()) {
                    interactable = exact; col = h.collider; point = h.point;
                    return true;
                }
            }

            // 2. Fallback magnétique : interactable à portée le plus proche du curseur à l'écran.
            if (!this.magneticTargetingEnabled || PlayerController.Local == null) return false;

            Vector3 playerPos = PlayerController.Local.transform.position;
            Collider[] candidates = Physics.OverlapSphere(playerPos, this.magneticCandidateRadius, this.InteractionMask, QueryTriggerInteraction.Ignore);
            if (candidates.Length == 0) return false;

            float refHeight = this.magneticToleranceReferenceHeight > 0f ? this.magneticToleranceReferenceHeight : 1080f;
            float tol = this.magneticScreenTolerancePixels * (Screen.height / refHeight);
            float tolSqr = tol * tol;
            Vector2 mouse = Input.mousePosition;
            float bestSqr = float.MaxValue;

            foreach (Collider c in candidates) {
                IInteractable cand = c.GetComponentInParent<IInteractable>();
                if (!cand.IsAlive() || !cand.IsInteractable()) continue;
                if (!WithinPlanarRange(playerPos, cand.transform.position, cand.GetRange())) continue;

                Vector3 sp = this.camera.WorldToScreenPoint(c.bounds.center);
                if (sp.z <= 0f) continue; // candidat derrière la caméra
                float dSqr = ((Vector2)sp - mouse).sqrMagnitude;
                if (dSqr > tolSqr || dSqr >= bestSqr) continue;

                bestSqr = dSqr;
                interactable = cand; col = c; point = c.bounds.center;
            }

            return interactable != null;
        }

        /// <summary>
        /// Applique le clic sur un interactable résolu (visée exacte OU magnétique) : ouvre le
        /// menu radial si à portée (CanInteractWith = portée + ligne de vue), sinon s'en approche.
        /// </summary>
        private void HandleInteractableClick(IInteractable interactable, Vector3 point, bool leftMouseClick) {
            bool canInteract = PlayerController.Local.CanInteractWith(interactable, point);
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
                PlayerController.Local.SetTarget(point, interactable, leftMouseClick, _pendingRunRequest);
            }
        }

        /// <summary>
        /// Per-frame hover highlight: raycasts under the cursor and outlines the
        /// IInteractable being pointed at (toggling the outline as the hovered target
        /// changes). Works for any IInteractable — props, doors, NPCs, items, job
        /// boards… — not just PropBehaviourBase.
        /// </summary>
        private void ManageHover() {
            HoverOutline target = null;
            string hoverName = null;

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUI) {
                // Résolution tolérante (visée exacte + magnétisme) : le même objet est surligné
                // au survol et interagi au clic.
                if (TryResolveInteractable(out IInteractable interactable, out Collider col, out _)) {
                    GameObject host = interactable.transform.gameObject;
                    target = host.GetComponent<HoverOutline>();
                    if (target == null) target = host.AddComponent<HoverOutline>();
                    hoverName = ResolveHoverName(col);
                } else {
                    // Joueurs distants / NPC : pas IInteractable (interaction au clic droit via
                    // GetContextActions) mais on veut quand même les surligner au survol. Grandes
                    // capsules → pas de souci d'ergonomie, un raycast exact dédié suffit.
                    Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit h, 100, this.InteractionMask, QueryTriggerInteraction.Ignore)) {
                        PlayerController pc = h.collider.GetComponentInParent<PlayerController>();
                        if (pc != null && !pc.isLocalPlayer) {
                            GameObject host = pc.gameObject;
                            target = host.GetComponent<HoverOutline>();
                            if (target == null) target = host.AddComponent<HoverOutline>();
                        }
                        hoverName = ResolveHoverName(h.collider);
                    }
                }
            }

            if (target != this._hoveredOutline) {
                if (this._hoveredOutline != null) this._hoveredOutline.Hide();
                this._hoveredOutline = target;
                if (this._hoveredOutline != null) this._hoveredOutline.Show();
            }

            if (!string.IsNullOrEmpty(hoverName)) HoverNameTooltip.Show(hoverName);
            else HoverNameTooltip.Hide();
        }

        /// <summary>
        /// Resolves the display name of a hovered character collider: remote
        /// players and NPCs. Returns null for the local player (no self tooltip),
        /// props, or anything else. NOTE: the PlayerController branch is the single
        /// gate point for the future V2 "give your identity" feature.
        /// </summary>
        private string ResolveHoverName(Collider col) {
            PlayerController pc = col.GetComponentInParent<PlayerController>();
            if (pc != null) {
                if (pc.isLocalPlayer) return null;
                // Identity is gated by relationship: revealed once at least
                // acquaintances, "Broz inconnu" otherwise.
                RelationshipState state = ClientRelationshipManager.Instance.GetState(pc.CharacterData?.Id);
                return state >= RelationshipState.Acquaintance ? pc.CharacterData?.Identity.FullName : "Broz inconnu";
            }

            ClientNpcView npc = col.GetComponentInParent<ClientNpcView>();
            if (npc != null) return $"[PNJ] {npc.FullName}";

            return null;
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

        // The Outline layer must always be in the camera culling mask so that props
        // moved to that layer by HoverOutline.Show() remain visible while outlined.
        private LayerMask WithOutlineLayer(LayerMask mask) {
            int l1 = LayerMask.NameToLayer(HoverOutline.OutlineLayerName);
            if (l1 >= 0) mask |= (1 << l1);
            int l2 = LayerMask.NameToLayer("MissionHighlight");
            if (l2 >= 0) mask |= (1 << l2);
            return mask;
        }

        public void SetCurrentMode(CameraModeEnum mode) {
            this.currentMode = mode;
            this.buildCamera.enabled = mode == CameraModeEnum.BUILD;
            this.tpsCamera.enabled = mode == CameraModeEnum.FREE;
            if (this.driveCamera != null) this.driveCamera.enabled = mode == CameraModeEnum.DRIVE;
            this.fpsCamera.SetActive(mode == CameraModeEnum.FPS);

            if (this.currentMode == CameraModeEnum.BUILD) {
                this.buildCamera.Setup(this.tpsCamera.GetVirtualCamera());
                this.camera.cullingMask = this.WithOutlineLayer(this.defaultCullingMask);
            } else if (this.currentMode == CameraModeEnum.FREE) {
                this.tpsCamera.Setup(this.buildCamera.GetVirtualCamera());
                this.camera.cullingMask = this.WithOutlineLayer(this.defaultCullingMask);
            } else if (this.currentMode == CameraModeEnum.DRIVE) {
                this.camera.cullingMask = this.WithOutlineLayer(this.defaultCullingMask);
            } else {
                this.camera.cullingMask = this.WithOutlineLayer(this.fpsCullingMask);
            }
        }

        /// <summary>Bascule sur la caméra de conduite dédiée, ciblant le véhicule donné.</summary>
        public void EnterVehicleCamera(VehicleController vehicle) {
            if (this.driveCamera == null) return;
            this.driveCamera.SetVehicle(vehicle);
            this.SetCurrentMode(CameraModeEnum.DRIVE);
        }

        /// <summary>Rend la main à la caméra "à pied" (FREE) à la sortie du véhicule.</summary>
        public void ExitVehicleCamera() {
            if (this.driveCamera != null) this.driveCamera.SetVehicle(null);
            this.SetCurrentMode(CameraModeEnum.FREE);
        }

        private void ManageInteraction() {
            if (SubGameController.IsActive) return;

            bool leftMouseClick = Input.GetMouseButtonUp(0);
            bool leftMousePressed = Input.GetMouseButton(0);
            bool rightMouseClick = Input.GetMouseButtonUp(1);

            if (Input.GetMouseButtonDown(0)) {
                this.startLeftClickValid = !EventSystem.current.IsPointerOverGameObject();
                if (this.startLeftClickValid) {
                    // Détection double clic : le 2e mouseDown dans la fenêtre arme
                    // _pendingRunRequest. Un simple clic le remet à false — le flag
                    // se réarme proprement à chaque nouveau cycle de clic.
                    bool isDouble = (Time.unscaledTime - _lastLeftDownTime) <= doubleClickWindow;
                    _pendingRunRequest = isDouble;
                    _lastLeftDownTime = Time.unscaledTime;
                }
            }

            if (((leftMouseClick || leftMousePressed) && !this.startLeftClickValid) || EventSystem.current.IsPointerOverGameObject()) {
                return;
            }

            Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);

            if (!(leftMouseClick || rightMouseClick || leftMousePressed)) return;

            // Résolution magnétique de l'interactable, sur le clic UP (pas le maintien gauche =
            // déplacement continu). Si un interactable à portée est proche du curseur, on l'aimante.
            // Exception : le clic droit visant un joueur distant (interaction sociale) garde la
            // priorité sur le magnétisme — les joueurs sont de grandes capsules, la visée exacte suffit.
            bool exactOnRemotePlayer = rightMouseClick && IsCursorOverRemotePlayer();
            if ((leftMouseClick || rightMouseClick) && !exactOnRemotePlayer) {
                if (TryResolveInteractable(out IInteractable magnet, out _, out Vector3 magnetPoint)) {
                    // Clic gauche : un interactable "clic droit uniquement" ne réagit pas → on
                    // laisse retomber sur le chemin RaycastAll (déplacement / objet derrière).
                    if (!(leftMouseClick && magnet.IsRightClickOnly())) {
                        HandleInteractableClick(magnet, magnetPoint, leftMouseClick);
                        return;
                    }
                }
            }

            // Ignore trigger colliders: props can host triggers for occupancy detection (e.g. DoorPropSource)
            // and we don't want those to intercept the click — only solid geometry should be hit.
            RaycastHit[] hits = Physics.RaycastAll(ray.origin, ray.direction, 100, this.InteractionMask, QueryTriggerInteraction.Ignore);
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
                        HandleInteractableClick(interactable, hit.point, leftMouseClick);
                    } else {
                        PlayerController.Local.SetTarget(hit.point, interactable, false, _pendingRunRequest);
                    }
                } else if (interactable == null && leftMousePressed && hit.collider.gameObject.layer.Equals(LayerMask.NameToLayer("Ground"))) {
                    PlayerController.Local.MoveTo(hit.point, _pendingRunRequest);
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
                        HUDManager.Instance.ShowContextMenu(player.GetContextActions(), player.transform);
                    }
                }
            }
        }
    }
}