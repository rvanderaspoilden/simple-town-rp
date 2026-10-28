using AI;
using AI.States;
using Interaction;
using Sim.Entities;
using Sim.Enums;
using Sim.Interactables;
using Sim.UI;
using Sim.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using Key = UnityEngine.InputSystem.Key;

namespace Sim {
    /// <summary>
    /// Pilote les modes caméra du jeu. Le mode « à pied » est le TPS épaule
    /// (<see cref="TpsShoulderCamera"/>, curseur verrouillé, interaction au crosshair) ;
    /// BUILD / DRIVE / FPS gardent leurs caméras dédiées et rendent la main au TPS en sortant.
    /// L'ancienne vue click-to-move (mode FREE, caméra orbitale + curseur magnétique) a été retirée.
    /// </summary>
    public class CameraManager : MonoBehaviour {
        [Header("Settings")]
        [FormerlySerializedAs("layerMaskInFreeMode")]
        [SerializeField]
        private LayerMask interactionLayerMask;

        [SerializeField]
        private LayerMask defaultCullingMask;

        [SerializeField]
        private LayerMask fpsCullingMask;

        [SerializeField]
        private GameObject fpsCamera;

        [Header("TPS (shoulder) mode")]
        [Tooltip("Touche d'interaction au crosshair (position physique, nouveau Input System). Appui court = action prioritaire de la cible ; appui long = menu radial complet.")]
        [SerializeField] private Key interactKey = Key.E;
        [Tooltip("Durée d'appui (s) sur la touche d'interaction au-delà de laquelle on ouvre le menu radial complet au lieu de déclencher l'action prioritaire.")]
        [SerializeField] private float interactLongPressDuration = 0.35f;
        [Tooltip("Touche pour lâcher immédiatement l'item tenu en main (action DROP de l'item). Position physique (nouveau Input System) : Key.Q = coin haut-gauche = touche étiquetée « A » sur clavier AZERTY — hors du cluster de déplacement sur tous les layouts.")]
        [SerializeField] private Key dropHeldItemKey = Key.Q;
        [Tooltip("Portée (m) du raycast crosshair centre-écran (nommage/outline). Le gate réel d'interaction reste CanInteractWith : portée de la config du prop/item + ligne de vue.")]
        [SerializeField] private float crosshairMaxDistance = 30f;
        [Tooltip("Rayon (m) du SphereCast d'aim-assist quand le ray exact ne touche rien d'interactable. 0 = désactivé.")]
        [SerializeField] private float crosshairAimAssistRadius = 0.25f;
        [Tooltip("Portée (m, plan XZ) pour ouvrir le menu contextuel d'un joueur distant visé au crosshair.")]
        [SerializeField] private float crosshairPlayerRange = 4f;
        [Tooltip("Maintenir cette touche libère le curseur pour interagir avec l'UI (la caméra et le crosshair sont suspendus pendant l'appui).")]
        [SerializeField] private Key freeCursorKey = Key.LeftAlt;

        private BuildCamera buildCamera;

        private DriveCamera driveCamera;

        private TpsShoulderCamera tpsShoulderCamera;

        private CameraModeEnum currentMode = CameraModeEnum.TPS;

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
            this.driveCamera = GetComponent<DriveCamera>();
            this.tpsShoulderCamera = GetComponent<TpsShoulderCamera>();

            this.buildCamera.enabled = false;
            if (this.driveCamera != null) this.driveCamera.enabled = false;
            if (this.tpsShoulderCamera != null) this.tpsShoulderCamera.enabled = false;
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

        void Update() {
            this.ApplyTpsCursorLock();

            if (PlayerController.Local == null || PlayerController.Local.PlayerState == PlayerState.DIED) return;

            // Renversé (ragdoll) : aucune interaction possible.
            if (PlayerController.Local.IsKnockedDown) {
                this.ClearHoverFeedback();
                return;
            }

            // Pendant la conduite, l'input est piloté par le VehicleController (WASD).
            // On neutralise l'interaction générique — mais on autorise le CLIC sur le
            // véhicule occupé pour ouvrir son menu contextuel (« Sortir », etc.).
            if (PlayerController.Local.IsDriving || PlayerController.Local.IsPassenger) {
                this.ManageVehicleOccupantInteraction();
                return;
            }

            if (this.currentMode == CameraModeEnum.TPS) {
                this.ManageCrosshairInteraction();
                this.ManageHeldItemQuickAction();
                this.ManageHeldItemDrop();
            } else {
                this.ClearHoverFeedback();
            }
        }

        private void ClearHoverFeedback() {
            if (this._hoveredOutline != null) {
                this._hoveredOutline.Hide();
                this._hoveredOutline = null;
            }
            HoverNameTooltip.Hide();
        }

        // ── Mode TPS épaule (verrou curseur + interaction crosshair) ──────────────────

        /// <summary>Verrouille le curseur en mode TPS tant qu'aucune UI bloquante n'est ouverte ;
        /// le libère sinon (et dans tous les autres modes). Pilote aussi la visibilité globale
        /// du crosshair.</summary>
        private void ApplyTpsCursorLock() {
            bool wantLocked = this.currentMode == CameraModeEnum.TPS
                              && PlayerController.Local != null
                              && PlayerController.Local.PlayerState != PlayerState.DIED
                              && !PlayerController.Local.IsKnockedDown
                              && !SubGameController.IsActive
                              && !GameInput.Held(this.freeCursorKey)
                              && !CursorLock.IsBlockingUiOpen();

            CursorLock.Apply(wantLocked);

            if (!wantLocked && CrosshairUI.Instance != null) CrosshairUI.Instance.Hide();
        }

        /// <summary>Raycast crosshair centre-écran : première touche qui n'appartient pas au
        /// joueur local (la caméra épaule peut raser sa propre capsule).</summary>
        private bool TryCrosshairHit(Ray ray, out RaycastHit hit) {
            hit = default;
            RaycastHit[] hits = Physics.RaycastAll(ray.origin, ray.direction, this.crosshairMaxDistance, this.InteractionMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return false;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit h in hits) {
                PlayerController pc = h.collider.GetComponentInParent<PlayerController>();
                if (pc != null && pc.isLocalPlayer) continue;
                hit = h;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Résout la cible au centre de l'écran (ray exact puis SphereCast d'aim-assist),
        /// surligne + nourrit le CrosshairUI, et ouvre le menu radial existant sur la touche
        /// d'interaction. Pas d'approche automatique : hors portée, la cible est nommée mais
        /// pas actionnable.
        /// </summary>
        private void ManageCrosshairInteraction() {
            if (SubGameController.IsActive || Cursor.lockState != CursorLockMode.Locked) {
                this.ClearHoverFeedback();
                this.ResetInteractHold();
                return;
            }

            IInteractable interactable = null;
            PlayerController remotePlayer = null;
            Collider targetCollider = null;
            Vector3 targetPoint = Vector3.zero;

            Ray ray = this.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (this.TryCrosshairHit(ray, out RaycastHit hit)) {
                targetCollider = hit.collider;
                targetPoint = hit.point;

                IInteractable exact = hit.collider.GetComponentInParent<IInteractable>();
                if (exact.IsAlive() && exact.IsInteractable()) {
                    interactable = exact;
                } else {
                    PlayerController pc = hit.collider.GetComponentInParent<PlayerController>();
                    if (pc != null && !pc.isLocalPlayer) remotePlayer = pc;
                }
            }

            // Aim-assist : si le ray exact ne vise rien d'utile, un SphereCast fin attrape
            // l'interactable presque centré.
            if (interactable == null && remotePlayer == null && this.crosshairAimAssistRadius > 0f
                && Physics.SphereCast(ray, this.crosshairAimAssistRadius, out RaycastHit assistHit, this.crosshairMaxDistance, this.InteractionMask, QueryTriggerInteraction.Ignore)) {
                IInteractable assist = assistHit.collider.GetComponentInParent<IInteractable>();
                PlayerController assistPc = assistHit.collider.GetComponentInParent<PlayerController>();
                if ((assistPc == null || !assistPc.isLocalPlayer) && assist.IsAlive() && assist.IsInteractable()) {
                    interactable = assist;
                    targetCollider = assistHit.collider;
                    targetPoint = assistHit.collider.bounds.center;
                }
            }

            HoverOutline outlineTarget = null;
            string hoverName = null;
            bool canInteract = false;

            if (interactable != null) {
                GameObject host = interactable.transform.gameObject;
                outlineTarget = host.GetComponent<HoverOutline>();
                if (outlineTarget == null) outlineTarget = host.AddComponent<HoverOutline>();
                hoverName = this.ResolveHoverName(targetCollider);
                // Viser cible, mais la distance d'interaction reste la portée définie dans la
                // config du prop/item (PropsConfig.rangeToInteract / ItemConfig.rangeToInteract)
                // via CanInteractWith (portée + ligne de vue), revalidée serveur.
                canInteract = PlayerController.Local.CanInteractWith(interactable, targetPoint);
            } else if (remotePlayer != null) {
                GameObject host = remotePlayer.gameObject;
                outlineTarget = host.GetComponent<HoverOutline>();
                if (outlineTarget == null) outlineTarget = host.AddComponent<HoverOutline>();
                hoverName = this.ResolveHoverName(targetCollider);
                canInteract = WithinPlanarRange(PlayerController.Local.transform.position, remotePlayer.transform.position, this.crosshairPlayerRange);
            }

            if (outlineTarget != this._hoveredOutline) {
                if (this._hoveredOutline != null) this._hoveredOutline.Hide();
                this._hoveredOutline = outlineTarget;
                if (this._hoveredOutline != null) this._hoveredOutline.Show();
            }

            // Le nom s'affiche sous le réticule (CrosshairUI), pas via le tooltip curseur.
            HoverNameTooltip.Hide();
            if (CrosshairUI.Instance != null) {
                // À la lunette (sniper), l'overlay porte son propre réticule plein écran.
                if (ScopeOverlayUI.IsScopeVisible) CrosshairUI.Instance.Hide();
                else if (interactable != null || remotePlayer != null) {
                    string promptLabel = canInteract ? this.ResolvePromptLabel(interactable) : null;
                    CrosshairUI.Instance.Show(hoverName, canInteract, promptLabel);
                }
                else CrosshairUI.Instance.ShowDefault();
            }

            // --- Touche d'interaction (E) -------------------------------------------
            // Joueur distant & PNJ : comportement inchangé — l'appui ouvre directement le
            // menu contextuel (pas d'action prioritaire configurable sur eux).
            if (interactable is ClientNpcView || (interactable == null && remotePlayer != null)) {
                this.ResetInteractHold();
                if (!canInteract || !GameInput.Down(this.interactKey)) return;

                // Un NPC visé est freezé immédiatement (le serveur stoppe son agent).
                if (interactable is ClientNpcView npcView) NpcInteractionSession.Begin(npcView);

                if (interactable != null) {
                    this.EnterInteractionPose(interactable.transform);
                    HUDManager.Instance.ShowContextMenu(interactable.GetActions(), interactable.transform);
                } else {
                    this.EnterInteractionPose(remotePlayer.transform);
                    HUDManager.Instance.ShowContextMenu(remotePlayer.GetContextActions(), remotePlayer.transform);
                }
                return;
            }

            if (interactable == null) { this.ResetInteractHold(); return; }

            // Props / portes / distributeurs / véhicules / cibles de mission :
            // appui COURT sur E = action prioritaire ; appui LONG = menu radial complet.
            this.ManageInteractHold(interactable, canInteract);
        }

        // ── Appui court (action prioritaire) / long (menu radial) sur la touche E ──

        private IInteractable _interactHoldTarget;
        private float _interactKeyDownTime = -1f;
        private bool _interactLongPressFired;

        private void ManageInteractHold(IInteractable interactable, bool canInteract) {
            if (!canInteract) { this.ResetInteractHold(); return; }

            if (GameInput.Down(this.interactKey)) {
                this._interactHoldTarget = interactable;
                this._interactKeyDownTime = Time.time;
                this._interactLongPressFired = false;
            }

            bool sameTarget = this._interactHoldTarget != null
                              && ReferenceEquals(this._interactHoldTarget, interactable);

            // Maintien au-delà du seuil → menu radial complet (une seule fois).
            if (sameTarget && !this._interactLongPressFired && GameInput.Held(this.interactKey)
                && Time.time - this._interactKeyDownTime >= this.interactLongPressDuration) {
                this._interactLongPressFired = true;
                this.EnterInteractionPose(interactable.transform);
                HUDManager.Instance.ShowContextMenu(interactable.GetActions(), interactable.transform);
            }

            // Relâché : appui court (avant seuil, même cible) = action prioritaire.
            if (GameInput.Up(this.interactKey)) {
                if (sameTarget && !this._interactLongPressFired) this.TriggerQuickAction(interactable);
                this.ResetInteractHold();
            }
        }

        private void TriggerQuickAction(IInteractable interactable) {
            this.EnterInteractionPose(interactable.transform);

            // Action prioritaire configurée (hors props « clic droit seulement ») → exécution directe.
            if (!interactable.IsRightClickOnly()
                && interactable is IQuickActionProvider provider
                && provider.TryGetQuickAction(interactable.GetActions(true), out Action quick)) {
                quick.Execute();
                return;
            }

            // Repli : aucune action prioritaire définie → menu radial complet.
            HUDManager.Instance.ShowContextMenu(interactable.GetActions(), interactable.transform);
        }

        /// <summary>Libellé affiché à côté du cadre de la touche d'interaction sous le réticule :
        /// le label de l'action prioritaire de la cible (ex. « S'asseoir », « Ouvrir », « Manger »)
        /// quand elle en définit une ; sinon le repli générique « Interagir » (l'appui court ouvre
        /// alors le menu radial). PNJ/joueurs distants et props « clic droit seulement » → générique.</summary>
        private string ResolvePromptLabel(IInteractable interactable) {
            if (interactable != null
                && !(interactable is ClientNpcView)
                && !interactable.IsRightClickOnly()
                && interactable is IQuickActionProvider provider
                && provider.TryGetQuickAction(interactable.GetActions(true), out Action quick)
                && quick != null && !string.IsNullOrEmpty(quick.Label)) {
                return quick.Label;
            }

            return "Interagir";
        }

        private void EnterInteractionPose(Transform target) {
            IState current = PlayerController.Local.CurrentState();
            if (current.GetType() == typeof(CharacterMove) || current.GetType() == typeof(CharacterFreeMove))
                PlayerController.Local.Idle();
            if (target != null) PlayerController.Local.LookAt(target);
        }

        private void ResetInteractHold() {
            this._interactHoldTarget = null;
            this._interactKeyDownTime = -1f;
            this._interactLongPressFired = false;
        }

        /// <summary>Clic gauche TPS avec un item en main : déclenche son action rapide
        /// (ex. manger). Les armes n'en ont pas → le tir reste géré par PlayerShooter.</summary>
        private void ManageHeldItemQuickAction() {
            if (Cursor.lockState != CursorLockMode.Locked || SubGameController.IsActive) return;
            if (PlayerController.Local == null || !Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            var hands = PlayerController.Local.PlayerHands;
            ItemBehaviour held = hands != null ? hands.RightHandItem : null;
            if (held != null) held.TryExecuteQuickAction();
        }

        /// <summary>Touche « lâcher » (A par défaut) en TPS : exécute l'action DROP de l'item tenu
        /// (main droite, sinon gauche). Route par l'action de l'item → même chemin que le bouton
        /// « Lâcher » de l'inventaire / le radial (respecte config + IsForbidden).</summary>
        private void ManageHeldItemDrop() {
            if (Cursor.lockState != CursorLockMode.Locked || SubGameController.IsActive) return;
            if (PlayerController.Local == null || !GameInput.Down(this.dropHeldItemKey)) return;

            var hands = PlayerController.Local.PlayerHands;
            if (hands == null) return;
            ItemBehaviour held = hands.RightHandItem != null ? hands.RightHandItem : hands.LeftHandItem;
            if (held != null) held.TryDrop();
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
                return this.interactionLayerMask | _outlineLayerBit | _missionLayerBit;
            }
        }

        private static bool WithinPlanarRange(Vector3 a, Vector3 b, float range) {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)) <= range;
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
            if (npc != null) return $"[{(npc.IsMerchant ? npc.MerchantLabel : "PNJ")}] {npc.FullName}";

            return null;
        }

        public CameraModeEnum GetMode() {
            return this.currentMode;
        }

        private void OnStateChanged(PlayerController player, StateType state) {
            if (player.isLocalPlayer) {
                this.SetCurrentMode(state == StateType.FREE ? CameraModeEnum.TPS : CameraModeEnum.BUILD);
            }
        }

        public void SetFpsCamera(Transform point) {
            this.fpsCamera.transform.position = point.position;
            this.fpsCamera.transform.rotation = point.rotation;
            this.SetCurrentMode(CameraModeEnum.FPS);
        }

        // Outline-driving layers must always be in the camera culling mask so that objects moved
        // onto them stay visible while outlined (the URP outline pass renders from the camera's
        // cull results): "Outline" (HoverOutline), "MissionHighlight", the placement-validity
        // layers "Place Valid" / "Place Invalid" (PlacementFeedback, item & prop placement),
        // and "Paint Hover" (PaintHoverOutline proxy that traces the hovered wall face / ground).
        private static readonly string[] OutlineDrivenLayers = {
            "MissionHighlight", "Place Valid", "Place Invalid", "Paint Hover"
        };

        private LayerMask WithOutlineLayer(LayerMask mask) {
            int l1 = LayerMask.NameToLayer(HoverOutline.OutlineLayerName);
            if (l1 >= 0) mask |= (1 << l1);
            foreach (string layerName in OutlineDrivenLayers) {
                int l = LayerMask.NameToLayer(layerName);
                if (l >= 0) mask |= (1 << l);
            }
            return mask;
        }

        public void SetCurrentMode(CameraModeEnum mode) {
            this.currentMode = mode;
            this.buildCamera.enabled = mode == CameraModeEnum.BUILD;
            if (this.driveCamera != null) this.driveCamera.enabled = mode == CameraModeEnum.DRIVE;
            if (this.tpsShoulderCamera != null) this.tpsShoulderCamera.enabled = mode == CameraModeEnum.TPS;
            this.fpsCamera.SetActive(mode == CameraModeEnum.FPS);

            // En TPS le joueur local quitte la NavMesh (CharacterController physique) ;
            // les autres modes le rattachent à l'agent (build, drive, bots).
            if (PlayerController.Local != null) PlayerController.Local.SetNavMeshBound(mode != CameraModeEnum.TPS);

            if (this.currentMode == CameraModeEnum.BUILD) {
                this.buildCamera.Setup();
                this.camera.cullingMask = this.WithOutlineLayer(this.defaultCullingMask);
            } else if (this.currentMode == CameraModeEnum.TPS) {
                this.tpsShoulderCamera.Setup();
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

        /// <summary>Rend la main à la caméra TPS à la sortie du véhicule.</summary>
        public void ExitVehicleCamera() {
            if (this.driveCamera != null) this.driveCamera.SetVehicle(null);
            this.SetCurrentMode(CameraModeEnum.TPS);
        }

        /// <summary>
        /// Interaction réduite quand le joueur local est À BORD d'un véhicule (conducteur ou passager).
        /// Le survol générique reste neutralisé (il se battrait avec la conduite), mais le CLIC GAUCHE
        /// sur le véhicule occupé ouvre son menu contextuel (« Sortir »).
        /// Le curseur reste libre en conduite (la vue se pilote à la molette), donc le clic est fiable.
        /// </summary>
        private void ManageVehicleOccupantInteraction() {
            VehicleController vehicle = PlayerController.Local.CurrentVehicle;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            HoverOutline target = null;
            if (vehicle != null && !overUI) {
                Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit h, 100, this.InteractionMask, QueryTriggerInteraction.Ignore)
                    && h.collider.GetComponentInParent<VehicleController>() == vehicle
                    && vehicle.IsInteractable()) {
                    target = vehicle.GetComponent<HoverOutline>();
                    if (target == null) target = vehicle.gameObject.AddComponent<HoverOutline>();

                    if (Input.GetMouseButtonUp(0))
                        HUDManager.Instance.ShowContextMenu(vehicle.GetActions(), vehicle.transform, true);
                }
            }

            if (target != this._hoveredOutline) {
                if (this._hoveredOutline != null) this._hoveredOutline.Hide();
                this._hoveredOutline = target;
                if (this._hoveredOutline != null) this._hoveredOutline.Show();
            }
            HoverNameTooltip.Hide();
        }
    }
}
