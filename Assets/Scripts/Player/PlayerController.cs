using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AI;
using AI.States;
using DG.Tweening;
using Interaction;
using Mirror;
using Sim.Entities;
using Sim.Entities.Persistence;
using Sim.Enums;
using Sim.Missions;
using Sim.Logging;
using Sim.Scriptables;
using Sim.UI;
using Sim.Utils;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using Action = Sim.Interactables.Action;
using Key = UnityEngine.InputSystem.Key;
using Random = UnityEngine.Random;

namespace Sim {
    public class PlayerController : NetworkBehaviour, ICharacterEntity {

        // ── ICharacterEntity ─────────────────────────────────────────────────
        // Le joueur utilise directement son netId comme OccupantId (le bit 31
        // n'est pas mis en pratique ; cf. CharacterEntityIds).
        public uint      OccupantId => netId;
        public bool      IsNpc      => false;
        public Transform Transform  => transform;

        [Header("Settings")]
        [SerializeField]
        private Transform headTargetForCamera;

        [SerializeField]
        private Vector3 idleHeadPosition;

        [SerializeField]
        private Vector3 sitHeadPosition;

        [SerializeField]
        private Vector3 sleepHeadPosition;

        [SerializeField]
        private Action[] actions;

        [SerializeField]
        private AudioClip walkStepSound;

        [SerializeField]
        private AudioClip eatSound;

        [SerializeField]
        private AudioSource audioSource;

        [SerializeField]
        private BubbleUI bubbleUI;

        [Header("Movement")]
        [Tooltip("Vitesse de marche normale (m/s) — appliquée au NavMeshAgent à chaque MoveTo non-running.")]
        [SerializeField] private float walkSpeed = 2f;
        [Tooltip("Vitesse de course (m/s) — appliquée quand MoveTo est appelée avec isRunning=true (double clic sur le sol).")]
        [SerializeField] private float runSpeed = 5.335f;
        [Range(0.2f, 1f)]
        [Tooltip("Facteur appliqué à la vitesse de MARCHE pendant la visée d'une arme (la course est coupée en visée).")]
        [SerializeField] private float aimWalkFactor = 0.6f;

        // Visée arme (PlayerShooter sur le même root) — ralentit la marche et coupe la course.
        private PlayerShooter playerShooter;

        // Nouveau Input System : Key = POSITION PHYSIQUE (W/A/S/D = ZQSD sur clavier AZERTY),
        // indépendant du layout — contrairement au KeyCode legacy (mapping US-QWERTY des lettres).
        [Header("Keyboard movement (physical WASD = ZQSD en AZERTY)")]
        [SerializeField] private Key forwardKey = Key.W;
        [SerializeField] private Key backKey = Key.S;
        [SerializeField] private Key leftKey = Key.A;
        [SerializeField] private Key rightKey = Key.D;
        [SerializeField] private Key runKey = Key.LeftShift;

        [Header("TPS movement (Starter Assets feel)")]
        [Tooltip("Temps de lissage (s) de la rotation du corps vers la direction de déplacement en mode TPS (SmoothDampAngle, valeur Starter Assets).")]
        [SerializeField] private float tpsRotationSmoothTime = 0.12f;
        [Tooltip("Taux de lissage de la vitesse (accélération/décélération) en mode TPS (valeur Starter Assets).")]
        [SerializeField] private float tpsSpeedChangeRate = 10f;

        // Vélocités internes du lissage TPS (SmoothDampAngle + lerp de vitesse).
        private float tpsRotationVelocity;
        private float tpsCurrentSpeed;
        private Vector3 tpsLastMoveDirection;

        [Header("TPS physics (unbound from NavMesh, Starter Assets values)")]
        [Tooltip("Gravité appliquée au CharacterController en mode TPS (Starter Assets : -15).")]
        [SerializeField] private float tpsGravity = -15f;
        [Tooltip("Décalage vertical du check de sol (Starter Assets : -0.14).")]
        [SerializeField] private float tpsGroundedOffset = -0.14f;
        [Tooltip("Rayon du CheckSphere de sol (Starter Assets : 0.28).")]
        [SerializeField] private float tpsGroundedRadius = 0.28f;
        [Tooltip("Layers considérés comme sol en TPS (Default + Ground + Wall).")]
        [SerializeField] private LayerMask tpsGroundLayers = (1 << 0) | (1 << 9) | (1 << 12);

        [Header("TPS jump (Starter Assets values)")]
        [Tooltip("Touche de saut en mode TPS.")]
        [SerializeField] private Key jumpKey = Key.Space;
        [Tooltip("Hauteur de saut (m) — Starter Assets : 1.2.")]
        [SerializeField] private float tpsJumpHeight = 1.2f;
        [Tooltip("Délai (s) avant de pouvoir resauter après un atterrissage — Starter Assets : 0.5.")]
        [SerializeField] private float tpsJumpTimeout = 0.5f;
        [Tooltip("Délai (s) en l'air avant de passer en chute libre (anim InAir) — évite le flicker dans les escaliers. Starter Assets : 0.15.")]
        [SerializeField] private float tpsFallTimeout = 0.15f;

        [Header("TPS crouch")]
        [Tooltip("Touche d'accroupissement (toggle) en mode TPS à pied. Espace relève aussi.")]
        [SerializeField] private Key crouchKey = Key.C;
        [Tooltip("Vitesse de déplacement accroupi (m/s) — doit matcher le threshold du blend tree Crouch.")]
        [SerializeField] private float crouchSpeed = 1.5f;
        [Tooltip("Hauteur du CharacterController accroupi (m) ; les valeurs debout sont lues au démarrage.")]
        [SerializeField] private float crouchControllerHeight = 1.2f;

        [Header("Stamina (endurance)")]
        [Tooltip("Endurance maximale (0-100). La barre se remplit à l'arrêt, se vide en courant et à chaque saut.")]
        [SerializeField] private float maxStamina = 100f;
        [Tooltip("Endurance consommée par seconde pendant la course.")]
        [SerializeField] private float staminaRunDrainPerSecond = 4f;
        [Tooltip("Endurance consommée à chaque saut.")]
        [SerializeField] private float staminaJumpCost = 15f;
        [Tooltip("Endurance régénérée par seconde quand le joueur est immobile.")]
        [SerializeField] private float staminaRegenPerSecond = 15f;
        [Tooltip("Délai (s) sans dépense avant que la régénération ne reprenne.")]
        [SerializeField] private float staminaRegenDelay = 1f;

        // Endurance locale (owner uniquement) : ressource de mouvement transitoire, non
        // persistée et non répliquée — la course et le saut sont déjà pilotés côté owner.
        private float stamina;
        private float staminaRegenCooldown;
        // Positionné chaque frame par TickFreeMove pour que TickStamina sache si le joueur
        // court activement (remis à false en tête d'Update).
        private bool freeMoveRunningThisFrame;

        /// <summary>Endurance restante ? La course n'est permise que tant qu'il reste de
        /// l'endurance ; à 0 le joueur retombe en marche.</summary>
        public bool CanRun => this.stamina > 0f;

        /// <summary>Assez d'endurance pour sauter (le saut coûte un montant fixe).</summary>
        public bool CanJump => this.stamina >= this.staminaJumpCost;

        // Valeurs debout du CharacterController, capturées dans Awake pour restaurer au relevage.
        private float standControllerHeight;
        private Vector3 standControllerCenter;

        // Mode TPS : le joueur est piloté par ce CharacterController (physique, non relié
        // à la NavMesh) ; le NavMeshAgent ne sert plus qu'aux déplacements scriptés (bots).
        private CharacterController characterController;
        private float tpsVerticalVelocity;
        private float tpsJumpTimeoutDelta;
        private float tpsFallTimeoutDelta;
        private const float TpsTerminalVelocity = 53f;

        // Bascule centralisée agent ↔ CharacterController : les states (sit, drive, KO…)
        // coupent la locomotion via SetLocomotionEnabled, la caméra choisit le mover via
        // SetNavMeshBound. ApplyLocomotion() réconcilie les deux.
        private bool locomotionEnabled = true;
        private bool navMeshBound = true;

        [Header("Only for debug")]
        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private StateType state;

        [SerializeField]
        private IInteractable interactableTarget;

        private Vector3 interactionOriginPoint;

        [SerializeField]
        private bool showRadialMenuWithPriority;

        [SerializeField]
        private CharacterData characterData; // represent all database info relative to the character

        [SerializeField]
        private Home characterHome;

        [SyncVar(hook = nameof(ParseCharacterData))]
        private string rawCharacterData;

        [SyncVar(hook = nameof(ParseCharacterHome))]
        private string rawCharacterHome;

        [SyncVar(hook = nameof(OnTalkingStateChanged))]
        private bool isTalking;

        [SyncVar(hook = nameof(OnWritingStateChanged))]
        private bool isWriting;

        [SyncVar]
        private PlayerState _playerState;

        // Renversé par un véhicule (ragdoll). Source de vérité serveur, répliquée pour les
        // late-joiners + le verrouillage du déplacement local. L'impulsion one-shot passe par
        // RpcKnockdown. Recovery automatique côté serveur après KnockdownDuration.
        [SyncVar(hook = nameof(OnKnockdownChanged))]
        private bool isKnockedDown;

        // Accroupi : posé par l'owner (toggle), répliqué pour les copies distantes et les
        // late-joiners (le NetworkAnimator réplique aussi le bool Crouch, mais le hook
        // pilote en plus la capsule et couvre le spawn tardif).
        [SyncVar(hook = nameof(OnCrouchChanged))]
        private bool isCrouching;

        public bool IsCrouching => isCrouching;

        /// <summary>Le joueur est-il actuellement renversé ? Consommé par CameraManager pour
        /// neutraliser le click-to-move / l'interaction (comme IsDriving).</summary>
        public bool IsKnockedDown => isKnockedDown;

        // Mort (PV à 0) : répliqué pour le ragdoll de mort sur toutes les copies (hook). Le
        // butin ne reste PAS sur le corps du joueur — il est lâché dans un SAC DE BUTIN
        // persistant (LootBag) décorrélé, qui survit au respawn. SEUL état logique de la
        // mort : dieState (posé par Die/TargetKill) — le hook ne fait que le visuel ragdoll.
        [SyncVar(hook = nameof(OnDeadChanged))]
        private bool isDead;

        /// <summary>Ce joueur est-il mort (ragdoll au sol avant réveil) ? Vrai sur toutes les
        /// copies — exclut la cible des armes.</summary>
        public bool IsDead => isDead;

        private const float KnockdownDuration = 3f;

        // Fenêtre (s) du ragdoll de mort avant le réveil automatique (Revive : PV reset,
        // -50 BC, téléport domicile). Court : le butin vit désormais dans un sac persistant.
        // Délai (s) avant le respawn AUTOMATIQUE après la mort. Le joueur peut réapparaître
        // avant via le bouton du HUD de mort (CmdRequestRespawn).
        private const float DeathReviveDelay = 180f;

        // Hidden on every client while the player teleports, so remote clients don't see
        // the NetworkTransform interpolate the position jump (the player slides). Toggled
        // around the reposition in SimpleTownNetwork.TeleportCoroutine.
        [SyncVar(hook = nameof(OnTeleportingChanged))]
        private bool _teleporting;

        [Command]
        public void CmdSetTeleporting(bool value) {
            this._teleporting = value;
        }

        private void OnTeleportingChanged(bool _, bool hidden) {
            this.SetVisualHidden(hidden);
        }

        /// <summary>Show/hide the character visuals without disabling GameObjects (keeps
        /// NetworkTransform, scripts and the NavMeshAgent running).</summary>
        private void SetVisualHidden(bool hidden) {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(false)) r.enabled = !hidden;
            foreach (Canvas c in GetComponentsInChildren<Canvas>(false)) c.enabled = !hidden;
        }

        // Replicated full-body action animation (SIT, SLEEP, DIE, BUILD…). The player
        // state machine only runs on the local player, so without this SyncVar remote
        // clients would never see the action pose nor the held-item hiding it triggers.
        [SyncVar(hook = nameof(OnAnimatorActionSynced))]
        private int _animatorActionSync;

        /// <summary>
        /// Set the current full-body action animation and replicate it to every client.
        /// The local player applies it immediately (prediction) then commands the server,
        /// which sets the SyncVar so remote copies play the same pose and apply the matching
        /// held-item visibility (PlayerAnimator.SetAction is the single chokepoint).
        /// </summary>
        public void SetAnimatorAction(CharacterAnimatorAction action) {
            this.animator.SetAction(action);
            if (isLocalPlayer) this.CmdSetAnimatorAction((int)action);
        }

        [Command]
        private void CmdSetAnimatorAction(int action) {
            this._animatorActionSync = action;
        }

        private void OnAnimatorActionSynced(int _, int newValue) {
            // The owner already applied it locally (prediction); only remote copies need this.
            if (!isLocalPlayer) this.animator.SetAction((CharacterAnimatorAction)newValue);
        }

        // Replicated drink gesture, targeted to a single arm: 0 = none, 1 = right, 2 = left.
        // Only the arm holding the consumed item plays Drink; the other hand keeps its anim.
        [SyncVar(hook = nameof(OnDrinkingHandSynced))]
        private int _drinkingHand;

        // Item awaiting consumption while the drink/eat gesture plays. The consume is driven by
        // the `OnConsumeBite` animation event so it lands exactly when the cup/food reaches the
        // mouth. Drink and Eat keep separate pending slots so they can later diverge onto their
        // own animations. -1 = none in progress. The gesture (and thus the event) only plays when
        // the holding arm is in Carry_Mug, so consumable configs must use the MUG carry shape.
        private int pendingDrinkEntityId = -1;
        private int pendingEatEntityId = -1;

        /// <summary>
        /// Start a one-shot drink gesture on the local player (replicated to every client).
        /// The item is consumed by the <see cref="OnConsumeBite"/> animation event fired when the
        /// cup reaches the mouth. Only the arm given by <paramref name="hand"/> plays the gesture
        /// (requires that item's CarryShape to be MUG).
        /// </summary>
        public void Drink(int entityId, HandType hand) {
            if (!isLocalPlayer) return;
            this.pendingDrinkEntityId = entityId;
            this.SetDrinkingHand(hand == HandType.Right ? 1 : 2);
            // Le son de gorgée démarre AVEC l'animation (geste levé), pas à la consommation
            // (OnConsumeBite). Répliqué à tous les clients qui voient le geste.
            this.CmdPlayDrinkSound();
        }

        /// <summary>
        /// Start a one-shot eat gesture on the local player (replicated to every client).
        /// Mirrors <see cref="Drink"/>: the item is consumed by the <see cref="OnConsumeBite"/>
        /// animation event fired when the food reaches the mouth. Reuses the drink gesture for now.
        /// </summary>
        public void Eat(int entityId, HandType hand) {
            if (!isLocalPlayer) return;
            this.pendingEatEntityId = entityId;
            this.SetDrinkingHand(hand == HandType.Right ? 1 : 2);
        }

        /// <summary>
        /// Animation event raised when the cup/food reaches the mouth. Dispatched on every client
        /// playing the gesture, but only the local owner consumes (the consume goes through a
        /// Command). Consumes whichever gesture (drink or eat) is in progress, then releases the arm.
        /// </summary>
        public void OnConsumeBite() {
            if (!isLocalPlayer) return;
            bool isDrink = this.pendingDrinkEntityId >= 0;
            int entityId = isDrink ? this.pendingDrinkEntityId : this.pendingEatEntityId;
            if (entityId < 0) return;
            this.pendingDrinkEntityId = -1;
            this.pendingEatEntityId = -1;
            this.ConsumeItem(entityId, isDrink);
            this.SetDrinkingHand(0);
        }

        private void SetDrinkingHand(int hand) {
            this.animator.SetDrinkingHand(hand);          // local prediction
            if (isLocalPlayer) this.CmdSetDrinkingHand(hand);
        }

        [Command]
        private void CmdSetDrinkingHand(int hand) {
            this._drinkingHand = hand;
        }

        private void OnDrinkingHandSynced(int _, int newValue) {
            if (!isLocalPlayer) this.animator.SetDrinkingHand(newValue);
        }

        // Server-only cache of the user's preferences. Hydrated by the server
        // in SetupCharacterCoroutine and updated via UserSettingsSyncMessage.
        // Lives outside SyncVar — only the server consults it (notif gate,
        // future server-side toggles).
        [System.NonSerialized] private UserSettingsData _userSettings = new UserSettingsData();
        public UserSettingsData UserSettings {
            get => _userSettings;
            set => _userSettings = value ?? new UserSettingsData();
        }

        private PlayerAnimator animator;

        private PlayerHands playerHands;

        private PlayerHealth playerHealth;

        private PlayerBankAccount playerBankAccount;

        private new Rigidbody rigidbody;

        private HashSet<GeographicArea> currentGeographicArea = new HashSet<GeographicArea>();

        private StateMachine stateMachine;

        private CharacterIdle idleState;

        private CharacterMove moveState;

        private CharacterFreeMove freeMoveState;

        private CharacterLookAt lookAtState;

        private CharacterInteract characterInteractState;

        private CharacterBusy busyState;

        private CharacterDie dieState;

        private CharacterKnockdown knockdownState;

        private CharacterStyleSetup characterStyleSetup;

        private RagdollController ragdoll;

        public delegate void StateChanged(PlayerController player, StateType state);

        public static event StateChanged OnStateChanged;

        public delegate void LocalPlayerStateChanged(PlayerState state);

        /// <summary>
        /// Fired when the local player's state-machine state changes (IDLE, MOVING,
        /// INTERACTING, ...). UI flows tied to a current interaction subscribe to
        /// this to close themselves when the state transitions away from INTERACTING.
        /// </summary>
        public static event LocalPlayerStateChanged OnLocalPlayerStateChanged;

        public delegate void CharacterDataChanged(CharacterData characterData);

        public static event CharacterDataChanged OnCharacterDataChanged;

        public static PlayerController Local;

        private void Awake() {
            this.navMeshAgent = GetComponent<NavMeshAgent>();
            this.characterController = GetComponent<CharacterController>();
            this.rigidbody = GetComponent<Rigidbody>();
            this.animator = GetComponent<PlayerAnimator>();
            this.playerHands = GetComponent<PlayerHands>();
            this.playerHealth = GetComponent<PlayerHealth>();
            this.playerBankAccount = GetComponent<PlayerBankAccount>();
            this.Collider = GetComponent<Collider>();
            this.characterStyleSetup = GetComponent<CharacterStyleSetup>();
            this.ragdoll = GetComponent<RagdollController>();
            this.playerShooter = GetComponent<PlayerShooter>();
            this._navPathScratch = new NavMeshPath();

            if (this.characterController != null) {
                this.standControllerHeight = this.characterController.height;
                this.standControllerCenter = this.characterController.center;
            }
        }

        public override void OnStartClient() {
            if (!isLocalPlayer) {
                this.navMeshAgent.enabled = false;
                this.rigidbody.useGravity = false;
                Destroy(GetComponent<AudioListener>());
                this.SetupActions();
                ClientLogger.NetworkDebug("RemotePlayerStartClient {NetId}", netId);
            }
        }

        public override void OnStartServer() {
            base.OnStartServer();
            GameLogger.Network.Debug("PlayerStartServer {NetId} {IsClient}", netId, isClient);

            if (!isClient) {
                this.navMeshAgent.enabled = false;
                this.rigidbody.useGravity = false;
                Destroy(GetComponent<AudioListener>());
            }

            Sim.Missions.MissionTargetHooks.RegisterPlayer(this);
        }

        public override void OnStopServer() {
            Sim.Missions.MissionTargetHooks.UnregisterPlayer(this);
            base.OnStopServer();
        }

        public override void OnStartLocalPlayer() {
            ClientLogger.Player("LocalPlayerStart {NetId} {CharacterName}", netId, characterData?.Identity.FullName ?? "unknown");
            
            this.InitStateMachine();

            this.stamina = this.maxStamina;

            if (this._playerState == PlayerState.DIED) {
                this.stateMachine.SetState(dieState);
            } else {
                this.stateMachine.SetState(idleState);
            }

            this.navMeshAgent.updateRotation = false;
            Local = this;
            // Full TPS : la caméra épaule est le seul mode « à pied » — on l'active dès le
            // spawn (débranche aussi l'agent NavMesh au profit du CharacterController).
            CameraManager.Instance.SetCurrentMode(CameraModeEnum.TPS);
            HUDManager.Instance.DisplayPanel(PanelTypeEnum.DEFAULT);
            PlayerStatsUI.Instance.UpdateStats(this.playerHealth.Health);

            // Hydrate the local relationship store so hover names are gated correctly.
            if (!string.IsNullOrEmpty(this.characterData?.Id)) {
                ApiManager.Instance.RetrieveRelationships(this.characterData.Id);
            }

            if (ClientPropManager.Instance == null) {
                ClientLogger.NetworkError(null, "ClientPropManagerNull {NetId}", netId);
            } else {
                ClientPropManager.Instance.EnterRoom("city");
                ClientLogger.Player("PlayerEnteredRoom {RoomId} {NetId}", "city", netId);
            }

            GetComponentInChildren<VoiceRoomAdapter>()?.OnLocalPlayerStart();
        }

        public override void OnStopClient() {
            if (isLocalPlayer) {
                ClientLogger.Player("LocalPlayerStop {NetId}", netId);
                this.UnSubscribeActions(this.actions);
                GetComponentInChildren<VoiceRoomAdapter>()?.OnLocalPlayerStop();
            }
        }

        public PlayerState PlayerState {
            get => _playerState;
            set {
                if (_playerState == value) return;
                _playerState = value;
                if (isLocalPlayer) OnLocalPlayerStateChanged?.Invoke(value);
            }
        }

        private void OnTriggerStay(Collider other) {
            if (isLocalPlayer && other.CompareTag("Geographic Area")) {
                SetGeographicArea(other.GetComponent<GeographicArea>());
            }
        }

        private void OnTriggerExit(Collider other) {
            if (isLocalPlayer && other.CompareTag("Geographic Area")) {
                RemoveGeographicArea(other.GetComponent<GeographicArea>());
            }
        }

        [Client]
        public void OnTalkingStateChanged(bool old, bool newValue) {
            this.isTalking = newValue;
            this.bubbleUI.SetVoiceBubbleVisibility(this.isTalking);
        }

        [Client]
        public void OnWritingStateChanged(bool old, bool newValue) {
            this.isWriting = newValue;
            if (this.bubbleUI != null) this.bubbleUI.SetWriting(this.isWriting);
        }

        public void PlayStepSound() {
            this.audioSource.volume = 0.005f;
            this.audioSource.pitch = Random.Range(1f, 1.2f);
            this.audioSource.PlayOneShot(this.walkStepSound);
        }

        public void ConsumeItem(int entityId, bool isDrink = false) {
            this.CmdConsumeItem(entityId, isDrink);
        }

        [Command]
        public void CmdConsumeItem(int entityId, bool isDrink) {
            GameLogger.Network.Debug("CmdConsumeItem {PlayerNetId} {EntityId}", netId, entityId);

            string roomId = PlayerRoomTracker.Instance.GetRoom(connectionToClient);
            if (roomId == null) {
                GameLogger.Network.Warning("CmdConsumeItemNoRoom {PlayerNetId}", netId);
                return;
            }

            ItemEntity entity = ServerItemManager.Instance.GetEntity(roomId, entityId);
            if (entity == null) {
                GameLogger.Network.Warning("CmdConsumeItemNotFound {PlayerNetId} {EntityId}", netId, entityId);
                return;
            }

            ItemConfig config = DatabaseManager.ItemConfigs.Find(x => x.ID == entity.ItemConfigId);
            if (config is ConsumableConfig consumableConfig) {
                this.playerHealth.ApplyModifications(consumableConfig.Impacts);
            }

            ServerItemManager.Instance.DespawnItem(roomId, entityId);

            GameLogger.Player.Info("PlayerConsumedItem {PlayerNetId} {EntityId} {ItemConfigId}",
                netId, entityId, entity.ItemConfigId);
            this.RpcConsume(isDrink);
        }

        public void CleanItem(int entityId) {
            this.CmdCleanItem(entityId);
        }

        [Command]
        public void CmdCleanItem(int entityId) {
            GameLogger.Network.Debug("CmdCleanItem {PlayerNetId} {EntityId}", netId, entityId);

            string roomId = PlayerRoomTracker.Instance.GetRoom(connectionToClient);
            if (roomId == null) {
                GameLogger.Network.Warning("CmdCleanItemNoRoom {PlayerNetId}", netId);
                return;
            }

            ItemEntity entity = ServerItemManager.Instance.GetEntity(roomId, entityId);
            if (entity == null) {
                GameLogger.Network.Warning("CmdCleanItemNotFound {PlayerNetId} {EntityId}", netId, entityId);
                return;
            }

            const int TrashBagConfigId = 101;
            ItemConfig bagCfg = DatabaseManager.GetItemConfigById(TrashBagConfigId);
            if (bagCfg == null) return;

            // Acquisition UNIVERSELLE : le sac va dans une main libre, sinon dans le colis
            // tenu, sinon échec — un seul point de contrôle (pas de check manuel ici). Le
            // débris n'est retiré QUE si le sac a pu être placé (onSuccess), pour ne jamais
            // perdre le débris sans donner le sac.
            int debrisEntityId = entityId;
            int debrisConfigId = entity.ItemConfigId;
            ServerItemManager.Instance.SpawnItemIntoInventory(connectionToClient, TrashBagConfigId, bagCfg, persistent: false,
                onSuccess: () => {
                    ServerItemManager.Instance.DespawnItem(roomId, debrisEntityId);
                    GameLogger.Player.Info("PlayerCleanedItem {PlayerNetId} {EntityId} {ItemConfigId}",
                        netId, debrisEntityId, debrisConfigId);
                },
                onFail: reason => {
                    GameLogger.Network.Warning("CmdCleanItemFail {PlayerNetId} {EntityId} {Reason}",
                        netId, debrisEntityId, reason);
                    TargetActionFailed(connectionToClient, reason);
                });
        }

        /// <summary>
        /// Feedback générique d'échec d'action serveur (pas de message S2C dédié) : affiche
        /// un toast au-dessus du joueur owner. Ex. CLEAN (sac ne tient pas) ou Emballer
        /// (colis plein / n'accepte pas les meubles). Appelable depuis le serveur (ex.
        /// ServerItemManager) via l'instance PlayerController de la connexion.
        /// </summary>
        [TargetRpc]
        public void TargetActionFailed(NetworkConnectionToClient target, string message) {
            if (!string.IsNullOrEmpty(message)) WorldToastManager.ShowError(message);
        }

        // Son de gorgée déclenché au début du geste de boire (cf. Drink), pour TOUS les clients.
        [Command]
        private void CmdPlayDrinkSound() => RpcPlayDrinkSound();

        [ClientRpc]
        private void RpcPlayDrinkSound() {
            Sim.Audio.AudioManager.Instance.Play(Sim.Audio.SfxId.Drink, transform.position);
        }

        [ClientRpc]
        public void RpcConsume(bool isDrink) {
            ClientLogger.NetworkDebug("RpcConsume {PlayerNetId} {IsLocalPlayer}", netId, isLocalPlayer);

            if (isLocalPlayer) {
                HUDManager.Instance.InventoryUI.Invoke(nameof(InventoryUI.UpdateUI), .1f);
                ClientLogger.UI("InventoryUpdateTriggered");
            }

            // Le son de gorgée joue déjà au début du geste (RpcPlayDrinkSound) ; ici on ne joue
            // que le son de bouchée (manger), au moment où la nourriture atteint la bouche.
            if (!isDrink) {
                Sim.Audio.AudioManager.Instance.Play(Sim.Audio.SfxId.Eat, transform.position);
                ClientLogger.Audio("ConsumeSoundPlayed {PlayerNetId} {IsDrink}", netId, isDrink);
            }
        }

        public void ResetGeographicArea() {
            this.currentGeographicArea.Clear();

            this.RefreshDefaultView();
        }

        private void SetGeographicArea(GeographicArea geographicArea) {
            if (this.currentGeographicArea.Contains(geographicArea)) return;

            this.currentGeographicArea.Add(geographicArea);

            this.currentGeographicArea = new HashSet<GeographicArea>(this.currentGeographicArea.OrderBy(x => x.PriorityOrder).ToList());

            this.RefreshDefaultView();
        }

        private void RemoveGeographicArea(GeographicArea geographicArea) {
            this.currentGeographicArea.Remove(geographicArea);

            this.RefreshDefaultView();
        }

        private void RefreshDefaultView() {
            if (!DefaultViewUI.Instance) return;

            GeographicArea current = CurrentGeographicArea;

            if (current) {
                DefaultViewUI.Instance.SetLocationText(current.LocationText);

                ApartmentController apt = current.Type == GeographicType.APARTMENT
                    ? current.GetComponentInParent<ApartmentController>() : null;
                if (apt != null) {
                    DefaultViewUI.Instance.SetTenantText($"Locataire: {apt.TenantFullName}");
                } else {
                    DefaultViewUI.Instance.SetTenantText(string.Empty);
                }
            } else {
                DefaultViewUI.Instance.SetLocationText(string.Empty);
                DefaultViewUI.Instance.SetTenantText(string.Empty);
            }

        }

        // Room transitions are authoritative via TeleportMessage.NewRoomId
        // (SimpleTownNetwork.OnTeleportPlayer → ClientPropManager.EnterRoom).
        // GeographicArea triggers are spatial sub-zones inside a single room
        // (e.g. apartment volumes inside hall:street:floor) and must NOT trigger
        // a room change — doing so would ClearProps() and despawn every prop on
        // the floor (hall doors, apt doors, delivery boxes…).

        public GeographicArea CurrentGeographicArea => currentGeographicArea.LastOrDefault();

        public PlayerHands PlayerHands => playerHands;

        public PlayerBankAccount PlayerBankAccount => playerBankAccount;

        [Server]
        public void SetRawCharacterData(string data) {
            this.rawCharacterData = data;
            this.characterData = JsonUtility.FromJson<CharacterData>(this.rawCharacterData);
            this.playerHealth.Init(this.characterData.Health);
            this.playerBankAccount.Init(this.characterData.Money);
        }

        /// <summary>
        /// Server-only career change. newProfessionId="" means resign (current → null).
        /// Persists to backend (start_or_resume + update_current_job) then
        /// rebroadcasts CharacterData. Active mission, if any, is abandoned.
        /// </summary>
        [Server]
        public void StartCareerChange(string newProfessionId) {
            StartCoroutine(CareerChangeCoroutine(newProfessionId ?? ""));
        }

        [Server]
        private IEnumerator CareerChangeCoroutine(string newProfessionId) {
            if (characterData == null || string.IsNullOrEmpty(characterData.Id)) {
                GameLogger.Network.Warning("CareerChangeSkipped_NoCharacter {NetId}", netId);
                yield break;
            }

            // Abandon any in-flight job; rewards system / cleanup handle the rest.
            MissionServerManager.Instance.OnPlayerDisconnected(netId);

            // Upsert the destination row when applying to a job (skip on resign).
            if (!string.IsNullOrEmpty(newProfessionId)) {
                var startBody = new CharacterJobStartRequest {
                    characterId = characterData.Id,
                    professionId = newProfessionId,
                };
                UnityWebRequest startReq = ApiManager.Instance.StartCharacterJobRequest(startBody);
                yield return startReq.SendWebRequest();
                if (startReq.responseCode != 200 && startReq.responseCode != 201) {
                    GameLogger.Network.Error(null, "CareerStartFailed {CharacterId} {Profession} {Code}",
                        characterData.Id, newProfessionId, startReq.responseCode);
                    yield break;
                }
                CharacterJobData row = JsonUtility.FromJson<CharacterJobData>(startReq.downloadHandler.text);
                if (row != null) MergeJob(row);
            }

            var updateBody = new CharacterUpdateCurrentJobRequest { currentProfessionId = newProfessionId };
            UnityWebRequest updateReq = ApiManager.Instance.UpdateCharacterCurrentJobRequest(characterData.Id, updateBody);
            yield return updateReq.SendWebRequest();
            if (updateReq.responseCode != 200) {
                GameLogger.Network.Error(null, "CareerUpdateCurrentMissionFailed {CharacterId} {NewProfession} {Code}",
                    characterData.Id, newProfessionId, updateReq.responseCode);
                yield break;
            }

            characterData.CurrentProfessionId = newProfessionId;
            SetRawCharacterData(JsonUtility.ToJson(characterData));
        }

        [Server]
        private void MergeJob(CharacterJobData row) {
            var list = characterData.Jobs;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].ProfessionId == row.ProfessionId) {
                    list[i] = row;
                    return;
                }
            }
            list.Add(row);
        }

        [Server]
        public void SetRawCharacterHome(string data) {
            this.rawCharacterHome = data;
            this.characterHome = string.IsNullOrEmpty(data) ? null : JsonUtility.FromJson<Home>(data);
        }

        /// <summary>Server-side: drops this character's home (e.g. rent eviction).
        /// The SyncVar hook refreshes the local player's address panel to
        /// "Sans domicile".</summary>
        [Server]
        public void ClearHome() {
            this.SetRawCharacterHome("{}");
        }

        public void ParseCharacterData(string old, string newValue) {
            this.characterData = JsonUtility.FromJson<CharacterData>(newValue);
            this.characterStyleSetup.ApplyStyle(this.CharacterData.Style);
            // Static event drives local-player HUD only — never broadcast a remote
            // player's data to it, or the local character panel gets overwritten.
            if (isLocalPlayer) OnCharacterDataChanged?.Invoke(this.characterData);
        }

        public string RawCharacterHome {
            get => rawCharacterHome;
            set => rawCharacterHome = value;
        }

        public void ParseCharacterHome(string old, string newValue) {
            this.characterHome = string.IsNullOrEmpty(newValue) ? null : JsonUtility.FromJson<Home>(newValue);
        }

        private void Update() {
            if (!isLocalPlayer || this.stateMachine == null) return;

            // Remis à false avant les ticks de mouvement : TickFreeMove le repositionne à
            // true uniquement quand le joueur court réellement cette frame.
            this.freeMoveRunningThisFrame = false;

            this.HandleKeyboardMovementEntry();

            this.HandleCrouchToggle();

            this.stateMachine.Tick();

            this.TickTpsGravity();

            this.TickStamina();

            // Voice push-to-talk indicator: the talking bubble follows the same "V" key
            // used by VoiceRoomAdapter to transmit voice.
            bool talkKeyHeld = GameInput.Held(Key.V);
            if (!this.isTalking && talkKeyHeld) {
                this.CmdSetTalk(true);
                this.bubbleUI.SetVoiceBubbleVisibility(true);
            } else if (this.isTalking && !talkKeyHeld) {
                this.CmdSetTalk(false);
                this.bubbleUI.SetVoiceBubbleVisibility(false);
            }

            // T opens the text-chat panel — but not while the player is typing in a text
            // field (chat, shop search, price input…), otherwise typing "t" would open chat.
            if (GameInput.Down(Key.T) && !IsTypingInInputField() && HUDManager.Instance != null) {
                HUDManager.Instance.ShowChatInput();
            }
        }

        /// <summary>True when a UI text input field currently has keyboard focus.</summary>
        private static bool IsTypingInInputField() {
            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == null) return false;
            if (sel.TryGetComponent(out TMPro.TMP_InputField tmp)) return tmp.isFocused;
            if (sel.TryGetComponent(out UnityEngine.UI.InputField legacy)) return legacy.isFocused;
            return false;
        }

        [Command]
        public void CmdSetTalk(bool value) {
            if (this.isTalking != value) {
                GameLogger.Network.Debug("CmdSetTalk {PlayerNetId} {Value}", netId, value);
                this.isTalking = value;
            }
        }

        // Real-time text chat — message is broadcast to every client and displayed
        // above the sender's head via BubbleUI.chatText. Nothing is persisted.
        private const int MaxChatLength = 120;

        /// <summary>Local-player typing indicator. Mirrored to all clients so the
        /// Write Bubble appears above the head while the user has the chat panel
        /// open.</summary>
        public void SetLocalWriting(bool writing) {
            if (this.bubbleUI != null) this.bubbleUI.SetWriting(writing);
            this.CmdSetWriting(writing);
        }

        [Command]
        private void CmdSetWriting(bool value) {
            if (this.isWriting != value) {
                this.isWriting = value;
            }
        }

        public void SendChatMessage(string message) {
            if (string.IsNullOrWhiteSpace(message)) return;
            this.CmdSendChat(message);

            // Persist the message for moderation tracking (fire-and-forget). The
            // in-world display goes through Mirror above; this only stores it,
            // attributed to the local player's character.
            if (ApiManager.Instance != null && this.characterData != null) {
                ApiManager.Instance.CreateChatMessage(
                    message.Trim(),
                    this.characterData.Id,
                    this.characterData.Identity.FullName);
            }
        }

        [Command]
        private void CmdSendChat(string message) {
            if (string.IsNullOrWhiteSpace(message)) return;
            message = message.Trim();
            if (message.Length > MaxChatLength) message = message.Substring(0, MaxChatLength);
            GameLogger.Network.Debug("CmdSendChat {PlayerNetId} {Length}", netId, message.Length);
            // Stop the typing indicator on every client (the SyncVar hook will
            // flip writeBubble off) before showing the message bubble.
            this.isWriting = false;
            this.RpcShowChatBubble(message);
        }

        [ClientRpc]
        private void RpcShowChatBubble(string message) {
            if (this.bubbleUI != null) {
                this.bubbleUI.ShowChatMessage(message);
            }
        }

        #region State Machine Management

        private void InitStateMachine() {
            this.stateMachine = new StateMachine();

            this.idleState = new CharacterIdle(this);
            this.moveState = new CharacterMove(this);
            this.freeMoveState = new CharacterFreeMove(this);
            this.lookAtState = new CharacterLookAt(this);
            this.characterInteractState = new CharacterInteract(this);
            this.busyState = new CharacterBusy(this);
            this.dieState = new CharacterDie(this);
            this.knockdownState = new CharacterKnockdown(this);

            this.stateMachine.AddTransition(moveState, idleState, HasReachedTargetPosition());
            this.stateMachine.AddTransition(lookAtState, idleState, HasLostTarget());
        }

        private Func<bool> HasReachedTargetPosition() => () => {
            // IInteractable is an interface: C# != null bypasses Unity's == override and returns true even
            // for destroyed MonoBehaviours. Cast to UnityEngine.Object to use Unity's destroyed-object check.
            if (this.interactableTarget != null && !(this.interactableTarget as UnityEngine.Object)) {
                Debug.Log("[Interaction] Target destroyed during interaction");
                Debug.Log("[Interaction] Cancelling current interaction safely");
                Debug.Log("[PlayerController] Cleared stale interactable reference");
                this.interactableTarget = null;
                return true; // force transition to idleState to reset movement cleanly
            }

            // Pour une cible mobile (NPC qui marche, joueur, véhicule en mouvement), on évalue la
            // portée + LoS contre la position COURANTE de la cible — pas le point figé au clic.
            // Pour les props statiques, on garde le point d'origine (la surface cliquée du prop).
            Vector3 evalPoint = (this.interactableTarget != null)
                ? this.interactableTarget.transform.position
                : this.interactionOriginPoint;

            return (this.interactableTarget != null &&
                    this.navMeshAgent.remainingDistance > this.navMeshAgent.stoppingDistance &&
                    this.CanInteractWith(this.interactableTarget, evalPoint)) ||
                   (!this.navMeshAgent.hasPath && MarkerController.Instance.IsActive());
        };

        private Func<bool> HasLostTarget() => () => this.lookAtState.Target == null;

        #endregion

        #region ACTIONS

        public Action[] Actions => actions;

        private Action _makeAcquaintanceAction;
        private Action _viewIdentityAction;
        private Action _makeContactAction;
        private Action _giveMoneyAction;
        private Action _muteAction;
        private Action _unmuteAction;

        /// <summary>
        /// Context-menu actions for THIS player as seen by the local player, gated
        /// by relationship state. Strangers → "Faire connaissance" ; connaissances →
        /// "Voir identité" + "Ajouter aux contacts" ; contacts → "Voir identité".
        /// Always offers "Rendre muet"/"Rétablir le son". Built on top of the prefab's
        /// base actions (e.g. LOOK / "Regarder").
        /// </summary>
        public Action[] GetContextActions() {
            // Mort : aucune interaction sur le corps lui-même (il disparaît au réveil) — le
            // butin est dans un SAC persistant au sol, fouillable séparément.
            if (this.isDead) return System.Array.Empty<Action>();

            List<Action> list = new List<Action>(this.actions);

            RelationshipState state = ClientRelationshipManager.Instance.GetState(this.characterData?.Id);

            if (state == RelationshipState.Unknown) {
                AddContextAction(list, ref _makeAcquaintanceAction, "MAKE_ACQUAINTANCE");
            } else {
                AddContextAction(list, ref _viewIdentityAction, "VIEW_IDENTITY");
                if (state == RelationshipState.Acquaintance) {
                    AddContextAction(list, ref _makeContactAction, "MAKE_CONTACT");
                }
            }

            // Available to anyone, including strangers (social-first pillar).
            AddContextAction(list, ref _giveMoneyAction, "GIVE_MONEY");

            // Local voice mute toggle — label/icône reflètent l'état courant (2 assets).
            if (IsLocallyMuted()) AddContextAction(list, ref _unmuteAction, "UNMUTE");
            else                  AddContextAction(list, ref _muteAction,   "MUTE");

            return list.ToArray();
        }

        private void AddContextAction(List<Action> list, ref Action cached, string resourceName) {
            Action a = EnsureAction(ref cached, resourceName);
            if (a != null) list.Add(a);
        }

        /// <summary>Clone une fois l'asset Action `Resources/Configurations/Actions/{name}`
        /// (label + sprite définis dans l'asset, comme les autres actions) et l'abonne.</summary>
        private Action EnsureAction(ref Action cached, string resourceName) {
            if (cached == null) {
                Action proto = Resources.Load<Action>($"Configurations/Actions/{resourceName}");
                if (proto == null) {
                    Debug.LogWarning($"[PlayerController] Action asset introuvable: Configurations/Actions/{resourceName}");
                    return null;
                }
                cached = Instantiate(proto);
                cached.OnExecute += DoAction;
            }
            return cached;
        }

        /// <summary>Client (initiator): asks to make acquaintance with a target player.</summary>
        public void SendAcquaintanceRequest(PlayerController target) {
            if (target == null || target == Local) return;
            NetworkClient.Send(new C2S_AcquaintanceRequest { targetNetId = target.netId });
        }

        /// <summary>Client (initiator): asks to add a target player to contacts.</summary>
        public void SendContactRequest(PlayerController target) {
            if (target == null || target == Local) return;
            NetworkClient.Send(new C2S_ContactRequest { targetNetId = target.netId });
        }

        // ── Local voice mute (Dissonance, client-only, not persisted) ──────────
        private Dissonance.VoicePlayerState ResolveVoicePlayer() {
            var mip = GetComponentInChildren<Dissonance.Integrations.MirrorIgnorance.MirrorIgnorancePlayer>();
            if (mip == null || string.IsNullOrEmpty(mip.PlayerId)) return null;
            var comms = Dissonance.DissonanceComms.GetSingleton();
            return comms != null ? comms.FindPlayer(mip.PlayerId) : null;
        }

        private bool IsLocallyMuted() {
            var vp = ResolveVoicePlayer();
            return vp != null && vp.IsLocallyMuted;
        }

        private void ToggleMute() {
            var vp = ResolveVoicePlayer();
            if (vp != null) vp.IsLocallyMuted = !vp.IsLocallyMuted;
        }

        public void SetupActions() {
            this.actions = this.actions.Where(x => x).Select(Instantiate).ToArray();
            this.SubscribeActions(this.actions);
        }

        private void SubscribeActions(Action[] actionList) {
            foreach (var action in actionList) {
                action.OnExecute += DoAction;
            }
        }

        private void UnSubscribeActions(Action[] actionList) {
            foreach (var action in actionList) {
                action.OnExecute -= DoAction;
            }
        }

        public void DoAction(Action action) {
            Debug.Log("do action : " + action.Label);

            switch (action.Type) {
                case ActionTypeEnum.LOOK:
                    Local.Look(this.transform);
                    break;
                case ActionTypeEnum.MAKE_ACQUAINTANCE:
                    Local.SendAcquaintanceRequest(this);
                    break;
                case ActionTypeEnum.MAKE_CONTACT:
                    Local.SendContactRequest(this);
                    break;
                case ActionTypeEnum.VIEW_IDENTITY:
                    IdentityCardUI.Instance.ShowFor(this);
                    break;
                case ActionTypeEnum.GIVE_MONEY:
                    Sim.UI.GiveMoneyInputUI.Instance?.Show(this);
                    break;
                case ActionTypeEnum.TOGGLE_MUTE:
                    this.ToggleMute();
                    break;
            }
        }

        /// <summary>Déplacement scripté via NavMeshAgent (bots de stress-test, marches
        /// automatisées). Le joueur humain se déplace au clavier en TPS — ce chemin n'est
        /// plus branché sur un clic. Nécessite un agent actif (SetNavMeshBound(true)).</summary>
        /// <param name="allowPartialPath">Le point visé peut être hors NavMesh → la garde de chemin
        /// est sautée et on laisse SetDestination snapper sur la NavMesh la plus proche et marcher
        /// au plus près. Sinon un chemin COMPLET est exigé (le joueur partirait vers un point
        /// inaccessible).</param>
        public void MoveTo(Vector3 targetPoint, bool isRunning = false, bool allowPartialPath = false) {
            // Pose assise/couchée : une commande de déplacement = « se lever et y aller ». On quitte
            // d'abord la pose (son OnExit réactive l'agent + le collider et restaure la position sur la
            // NavMesh) AVANT la garde agent.enabled ci-dessous — sinon le clic serait ignoré et le
            // joueur resterait coincé sur le siège.
            if (this.PlayerState == PlayerState.SITTING || this.PlayerState == PlayerState.SLEEPING) {
                this.ExitPose();
            }

            // Agent désactivé (knockdown, conduite, mort, hors NavMesh) : on ignore silencieusement.
            // Pas une erreur utilisateur — pas de toast.
            if (this.navMeshAgent == null || !this.navMeshAgent.enabled || !this.navMeshAgent.isOnNavMesh) return;

            // Clic au sol : on précalcule le chemin et on exige PathComplete — sinon le joueur
            // partirait silencieusement vers un point inaccessible (derrière un mur, île NavMesh isolée).
            // Pour une INTERACTION (allowPartialPath) on saute cette garde : le point visé est la surface
            // d'un prop, souvent HORS NavMesh, où CalculatePath échoue (PathInvalid). On laisse alors
            // SetDestination snapper sur la NavMesh la plus proche et marcher au plus près — la portée
            // réelle est validée ensuite par CanInteractWith (qui déclenche l'ouverture du menu).
            if (!allowPartialPath
                && (!this.navMeshAgent.CalculatePath(targetPoint, _navPathScratch)
                    || _navPathScratch.status != NavMeshPathStatus.PathComplete)) {
                return;
            }

            this.stateMachine.SetState(moveState);
            this.navMeshAgent.speed = (isRunning ? runSpeed : walkSpeed) * MoveSpeedPerkMultiplier();
            this.navMeshAgent.SetDestination(targetPoint);

            HUDManager.Instance.CloseInventory();
        }

        /// <summary>Sort d'une pose assise/couchée en repassant par idle : l'OnExit du state (CharacterSit
        /// /CharacterSleep) réactive le NavMeshAgent + le collider et restaure la position d'avant la pose.
        /// On neutralise temporairement la cible d'interaction pour empêcher CharacterIdle.OnEnter d'ouvrir
        /// un menu (ou de purger la cible) au passage — le flux « marcher jusqu'au prop puis interagir »
        /// a encore besoin de cette cible une fois la marche engagée.</summary>
        private void ExitPose() {
            IInteractable pending = this.interactableTarget;
            this.interactableTarget = null;
            this.Idle();
            this.interactableTarget = pending;
        }

        // Réutilisé par MoveTo pour éviter une alloc à chaque clic. NavMeshPath NE PEUT PAS être
        // construit dans un field initializer ni un constructeur Unity — il faut attendre Awake
        // (sinon la ref native sous-jacente est nulle et CalculatePath jette une NRE).
        private NavMeshPath _navPathScratch;

        // Bonus de vitesse passif issu des nœuds de constellation débloqués (ex. nœud
        // Vitesse du Livreur). Lu sur le provider du joueur LOCAL — le mouvement est
        // piloté par l'owner. 1.0 si aucun bonus / provider non hydraté.
        private float MoveSpeedPerkMultiplier() {
            var pc = GetComponent<Sim.Player.PlayerConstellation>();
            var provider = pc != null ? pc.Provider : null;
            if (provider == null) return 1f;
            return Sim.Constellation.ConstellationPerks.MoveSpeedMultiplier(provider.State.IsUnlocked);
        }

        /// <summary>Régénère / consomme l'endurance du joueur local et pousse la valeur à
        /// la barre « énergie » de l'HUD. Appelée chaque frame après les ticks de mouvement :
        /// course = drain, immobile = régénération (après un court délai), marche = statu quo.
        /// Le coût du saut est prélevé directement dans TickTpsGravity.</summary>
        private void TickStamina() {
            float dt = Time.deltaTime;
            bool moving = this.tpsCurrentSpeed > 0.1f;

            if (this.freeMoveRunningThisFrame && moving) {
                // Seule la course vide l'endurance.
                this.stamina = Mathf.Max(0f, this.stamina - this.staminaRunDrainPerSecond * dt);
                this.staminaRegenCooldown = this.staminaRegenDelay;
            } else {
                // Immobile ou en marche : la barre se remplit (après le délai anti-régénération immédiate).
                if (this.staminaRegenCooldown > 0f) {
                    this.staminaRegenCooldown -= dt;
                } else {
                    this.stamina = Mathf.Min(this.maxStamina, this.stamina + this.staminaRegenPerSecond * dt);
                }
            }

            if (PlayerStatsUI.Instance != null) {
                float value = this.maxStamina > 0f ? this.stamina / this.maxStamina * CommonConstants.MAX_BAR_AMOUNT : 0f;
                PlayerStatsUI.Instance.SetEnergy(value);
            }
        }

        // ── Déplacement clavier direct (ZQSD) — expérimental ─────────────────────────

        /// <summary>Lit l'axe directionnel ZQSD brut : x = D(droite) − Q(gauche), y = Z(avant) − S(arrière).</summary>
        public Vector2 ReadMoveAxis() {
            float x = 0f, y = 0f;
            if (GameInput.Held(forwardKey)) y += 1f;
            if (GameInput.Held(backKey)) y -= 1f;
            if (GameInput.Held(rightKey)) x += 1f;
            if (GameInput.Held(leftKey)) x -= 1f;
            return new Vector2(x, y);
        }

        /// <summary>Engage l'état de déplacement clavier dès qu'un input ZQSD est détecté.
        /// Bouger QUITTE l'état en cours (LookAt, Interact, build chronométré, pose
        /// assise/couchée via ExitPose) — leurs OnExit font le ménage. Neutralisé pendant
        /// la frappe UI, le knockdown, la conduite et la mort.</summary>
        private void HandleKeyboardMovementEntry() {
            if (this.stateMachine.CurrentState == freeMoveState) return; // déjà en déplacement clavier
            if (IsTypingInInputField()) return;
            if (this.isKnockedDown || this.IsDriving || this.IsPassenger) return;
            if (this._playerState == PlayerState.DIED) return;

            // Le clavier ne doit pas faire bouger le joueur en BUILD/DRIVE/FPS : seul le
            // mode TPS épaule est piloté au clavier.
            if (CameraManager.Instance != null && CameraManager.Instance.GetMode() != CameraModeEnum.TPS) return;

            if (this.ReadMoveAxis().sqrMagnitude <= 0.01f) return;

            // Modale marchande ouverte : elle bloque aussi les interactions — on ne
            // s'échappe pas du shop en marchant.
            if (Sim.UI.MerchantShopUI.Instance != null && Sim.UI.MerchantShopUI.Instance.gameObject.activeSelf) return;

            // Écran ATM ouvert : le joueur reste planté devant le distributeur — la session
            // se quitte par Quitter/Échap (ou l'éloignement forcé type téléport, géré par AtmUI).
            if (Sim.UI.AtmUI.Instance != null && Sim.UI.AtmUI.Instance.IsOpen) return;

            // Dialogue NPC ouvert : bouger le quitte proprement (Hide libère la session NPC).
            if (Sim.UI.DialogueUI.Instance != null && Sim.UI.DialogueUI.Instance.gameObject.activeSelf) {
                Sim.UI.DialogueUI.Instance.Hide();
            }

            // Pose assise/couchée : « se lever et y aller », comme MoveTo (ExitPose réactive la
            // locomotion et restaure la position).
            if (this.PlayerState == PlayerState.SITTING || this.PlayerState == PlayerState.SLEEPING) {
                this.ExitPose();
            }

            // Locomotion coupée (téléport en cours…) : pas de déplacement.
            if (!this.locomotionEnabled) return;

            // Depuis n'importe quel autre état (LookAt après un NPC ou « Regarder », Interact,
            // build chronométré, click-to-move…) : l'entrée en déplacement déclenche leurs OnExit
            // (StopInteraction, annulation du build, fermeture du radial via Idle.OnExit).
            this.stateMachine.SetState(freeMoveState);
        }

        // ── Accroupissement (TPS à pied uniquement) ──────────────────────────────────

        /// <summary>Toggle crouch du joueur local : seulement en TPS à pied (CharacterController
        /// actif), curseur verrouillé, hors frappe UI, knockdown, conduite et mort.</summary>
        private void HandleCrouchToggle() {
            if (!GameInput.Down(this.crouchKey)) return;
            if (Cursor.lockState != CursorLockMode.Locked || IsTypingInInputField()) return;
            if (this.isKnockedDown || this.IsDriving || this.IsPassenger) return;
            if (this._playerState == PlayerState.DIED) return;
            if (this.characterController == null || !this.characterController.enabled) return;

            this.SetCrouch(!this.isCrouching);
        }

        /// <summary>Pose l'état accroupi côté owner : anim + capsule immédiates (prédiction),
        /// puis réplication via le SyncVar. Les copies distantes passent par le hook.</summary>
        private void SetCrouch(bool value) {
            if (!isLocalPlayer || this.isCrouching == value) return;
            this.animator.SetCrouch(value);
            this.ApplyCrouchCapsule(value);
            this.CmdSetCrouch(value);
        }

        [Command]
        private void CmdSetCrouch(bool value) {
            this.isCrouching = value;
        }

        private void OnCrouchChanged(bool _, bool newValue) {
            // L'owner a déjà appliqué localement (prédiction) ; seules les copies
            // distantes rejouent l'anim et la capsule ici.
            if (isLocalPlayer) return;
            this.animator.SetCrouch(newValue);
            this.ApplyCrouchCapsule(newValue);
        }

        /// <summary>Abaisse/restaure le CharacterController en gardant les pieds au sol
        /// (le centre descend de la moitié de la différence de hauteur).</summary>
        private void ApplyCrouchCapsule(bool crouched) {
            if (this.characterController == null || this.standControllerHeight <= 0f) return;
            float height = crouched ? this.crouchControllerHeight : this.standControllerHeight;
            this.characterController.height = height;
            this.characterController.center = this.standControllerCenter
                + Vector3.down * ((this.standControllerHeight - height) * 0.5f);
        }

        // ── Locomotion : NavMeshAgent (build/bots) ↔ CharacterController (TPS) ───────

        /// <summary>Choisit le mover du joueur local : true = NavMeshAgent (build, drive, bots),
        /// false = CharacterController physique (TPS, non relié à la NavMesh). Appelé par
        /// CameraManager à chaque changement de mode caméra.</summary>
        public void SetNavMeshBound(bool bound) {
            if (this.navMeshBound == bound) return;
            this.navMeshBound = bound;

            // Une marche agent en cours ne peut pas survivre sans agent : on coupe le
            // path et on retombe sur idle avant de désactiver l'agent.
            if (!bound && this.navMeshAgent != null && this.navMeshAgent.enabled) {
                if (this.navMeshAgent.hasPath) this.navMeshAgent.ResetPath();
                if (this.stateMachine != null && this.stateMachine.CurrentState == this.moveState) this.Idle();
            }

            this.ApplyLocomotion();
        }

        /// <summary>Coupe/rétablit la locomotion du joueur local (remplace les accès directs
        /// NavMeshAgent.enabled des states sit/drive/KO/teleport…) : le bon mover est
        /// réactivé selon le mode caméra courant.</summary>
        public void SetLocomotionEnabled(bool enabled) {
            this.locomotionEnabled = enabled;
            this.ApplyLocomotion();
        }

        private void ApplyLocomotion() {
            bool agentOn = this.locomotionEnabled && this.navMeshBound;
            bool controllerOn = this.locomotionEnabled && !this.navMeshBound && this.characterController != null;

            if (this.navMeshAgent != null && this.navMeshAgent.enabled != agentOn) {
                // Réactivation de l'agent : le joueur a pu quitter la NavMesh en TPS — on le
                // recale sur le point de mesh le plus proche pour éviter un agent off-mesh.
                if (agentOn && NavMesh.SamplePosition(this.transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) {
                    this.transform.position = hit.position;
                }
                this.navMeshAgent.enabled = agentOn;
            }

            if (this.characterController != null && this.characterController.enabled != controllerOn) {
                // Activation du CC : la position courante vient parfois d'être posée pour un agent
                // navmesh (téléport) et peut être incrustée dans le sol — un CC incrusté ne peut
                // plus bouger (la dépénétration bloque Move). On recale sur la NavMesh avec une
                // petite marge verticale avant d'activer, symétrique du snap agent ci-dessus.
                if (controllerOn && NavMesh.SamplePosition(this.transform.position, out NavMeshHit ccHit, 3f, NavMesh.AllAreas)) {
                    this.transform.position = ccHit.position + Vector3.up * 0.05f;
                }
                this.characterController.enabled = controllerOn;
            }

            if (!controllerOn) {
                this.tpsVerticalVelocity = 0f;
                // Remet l'animator au sol : sans CC, Grounded n'est plus mis à jour et un
                // FreeFall resté à true bloquerait l'état InAir.
                if (this.animator != null) {
                    this.animator.SetGrounded(true);
                    this.animator.SetJump(false);
                    this.animator.SetFreeFall(false);
                }
                // Quitter le TPS à pied (sit, drive, build, téléport…) relève toujours :
                // aucun de ces états ne se joue accroupi.
                if (isLocalPlayer) this.SetCrouch(false);
            }
        }

        /// <summary>Gravité + saut façon Starter Assets (JumpAndGravity), appliqués tant que le
        /// CharacterController est actif (même à l'arrêt : marcher hors d'un rebord en idle doit
        /// faire tomber). Alimente les bools Grounded/Jump/FreeFall de l'animator.</summary>
        private void TickTpsGravity() {
            if (this.characterController == null || !this.characterController.enabled) return;

            Vector3 spherePosition = new Vector3(this.transform.position.x,
                this.transform.position.y - this.tpsGroundedOffset, this.transform.position.z);
            bool grounded = Physics.CheckSphere(spherePosition, this.tpsGroundedRadius,
                this.tpsGroundLayers, QueryTriggerInteraction.Ignore);
            this.animator.SetGrounded(grounded);

            if (grounded) {
                this.tpsFallTimeoutDelta = this.tpsFallTimeout;
                this.animator.SetJump(false);
                this.animator.SetFreeFall(false);

                if (this.tpsVerticalVelocity < 0f) {
                    this.tpsVerticalVelocity = -2f; // colle au sol sans accumuler (valeur Starter Assets)
                }

                // Saut : seulement curseur verrouillé (pas de saut en tapant Espace dans une UI).
                if (this.tpsJumpTimeoutDelta <= 0f
                    && GameInput.Held(this.jumpKey)
                    && Cursor.lockState == CursorLockMode.Locked
                    && !IsTypingInInputField()) {
                    if (this.isCrouching) {
                        // Accroupi : Espace relève au lieu de sauter.
                        this.SetCrouch(false);
                    } else if (this.CanJump) {
                        // Vitesse verticale nécessaire pour atteindre la hauteur voulue : v = √(h × -2g).
                        this.tpsVerticalVelocity = Mathf.Sqrt(this.tpsJumpHeight * -2f * this.tpsGravity);
                        this.animator.SetJump(true);
                        // Le saut consomme de l'endurance et repousse la régénération.
                        this.stamina = Mathf.Max(0f, this.stamina - this.staminaJumpCost);
                        this.staminaRegenCooldown = this.staminaRegenDelay;
                    }
                    // Endurance insuffisante : le saut ne se déclenche pas.
                }

                if (this.tpsJumpTimeoutDelta >= 0f) {
                    this.tpsJumpTimeoutDelta -= Time.deltaTime;
                }
            } else {
                this.tpsJumpTimeoutDelta = this.tpsJumpTimeout;

                // Chute libre après un court délai (évite le flicker InAir dans les escaliers).
                if (this.tpsFallTimeoutDelta >= 0f) {
                    this.tpsFallTimeoutDelta -= Time.deltaTime;
                } else {
                    this.animator.SetFreeFall(true);
                }
            }

            if (this.tpsVerticalVelocity > -TpsTerminalVelocity) {
                this.tpsVerticalVelocity += this.tpsGravity * Time.deltaTime;
            }

            this.characterController.Move(new Vector3(0f, this.tpsVerticalVelocity, 0f) * Time.deltaTime);
        }

        /// <summary>Pilote le personnage en ZQSD relatif à la caméra TPS (CharacterController,
        /// physique libre). Renvoie false en l'absence d'input directionnel — l'état
        /// CharacterFreeMove retombe alors sur idle. Course avec la touche dédiée (Maj).
        /// Owner local uniquement.</summary>
        public bool TickFreeMove() {
            // Sortie immédiate si le mode caméra a quitté le TPS en cours de déplacement (entrée
            // build, passager, FPS) : on retombe en idle pour ne pas continuer à driver le mover.
            CameraModeEnum camMode = CameraManager.Instance != null ? CameraManager.Instance.GetMode() : CameraModeEnum.TPS;
            if (camMode != CameraModeEnum.TPS) {
                this.ResetFreeMove();
                return false;
            }

            Vector2 axis = this.ReadMoveAxis();
            if (axis.sqrMagnitude < 0.01f) {
                // Feel Starter Assets : relâcher ZQSD ne stoppe pas net — la vitesse décélère
                // en glissant sur le dernier cap, puis l'état retombe sur idle.
                if (this.tpsCurrentSpeed > 0.1f && this.tpsLastMoveDirection.sqrMagnitude > 0.0001f) {
                    this.tpsCurrentSpeed = Mathf.Lerp(this.tpsCurrentSpeed, 0f, Time.deltaTime * this.tpsSpeedChangeRate);
                    this.MoveOnFoot(this.tpsLastMoveDirection * this.tpsCurrentSpeed * Time.deltaTime);
                    this.animator.SetVelocity(this.tpsCurrentSpeed);
                    return true;
                }
                this.ResetFreeMove();
                return false;
            }

            // Base caméra aplatie sur le plan horizontal : ZQSD est relatif à l'orientation de la vue.
            Transform cam = CameraManager.Instance != null ? CameraManager.Instance.Camera.transform : null;
            Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = cam != null ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;

            Vector3 dir = forward * axis.y + right * axis.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            if (dir.sqrMagnitude < 0.0001f) {
                this.ResetFreeMove();
                return false;
            }

            // Visée arme : course coupée et marche ralentie (précision + immersion).
            // Accroupi : vitesse dédiée, course coupée.
            bool aimingWeapon = this.playerShooter != null && this.playerShooter.IsAiming;
            // La course exige de l'endurance : épuisé, le joueur retombe automatiquement en marche.
            bool running = !aimingWeapon && !this.isCrouching && GameInput.Held(runKey) && this.CanRun;
            this.freeMoveRunningThisFrame = running;
            float baseSpeed = this.isCrouching ? this.crouchSpeed : (running ? runSpeed : walkSpeed);
            float targetSpeed = baseSpeed * MoveSpeedPerkMultiplier();
            if (aimingWeapon && !this.isCrouching) targetSpeed *= this.aimWalkFactor;

            // Feel Starter Assets : la vitesse accélère/décélère progressivement au lieu de
            // sauter instantanément entre marche et course (bande de snap 0.1 comme chez eux).
            this.tpsCurrentSpeed = Mathf.Lerp(this.tpsCurrentSpeed, targetSpeed, Time.deltaTime * this.tpsSpeedChangeRate);
            if (Mathf.Abs(this.tpsCurrentSpeed - targetSpeed) < 0.1f) this.tpsCurrentSpeed = targetSpeed;
            this.tpsLastMoveDirection = dir.normalized;

            this.MoveOnFoot(dir * this.tpsCurrentSpeed * Time.deltaTime);

            // Feel Starter Assets : rotation lissée (SmoothDampAngle) vers le cap de déplacement.
            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yaw = Mathf.SmoothDampAngle(this.transform.eulerAngles.y, targetYaw, ref this.tpsRotationVelocity, this.tpsRotationSmoothTime);
            this.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            this.animator.SetVelocity(this.tpsCurrentSpeed);
            return true;
        }

        /// <summary>Applique un déplacement horizontal au bon mover : CharacterController quand il
        /// est actif (TPS, physique libre), sinon NavMeshAgent (fallback, contraint à la NavMesh).</summary>
        private void MoveOnFoot(Vector3 planarMotion) {
            if (this.characterController != null && this.characterController.enabled) {
                this.characterController.Move(planarMotion);
            } else if (this.navMeshAgent != null && this.navMeshAgent.enabled && this.navMeshAgent.isOnNavMesh) {
                this.navMeshAgent.Move(planarMotion);
            }
        }

        /// <summary>Remet à zéro l'animator et les vélocités de lissage TPS quand le déplacement
        /// clavier s'arrête ou est interrompu.</summary>
        private void ResetFreeMove() {
            this.animator.SetVelocity(0f);
            this.tpsCurrentSpeed = 0f;
            this.tpsRotationVelocity = 0f;
            this.tpsLastMoveDirection = Vector3.zero;
        }

        public void LookAt(Transform target) {
            Vector3 dir = target.position - this.transform.position;
            this.transform.DORotateQuaternion(Quaternion.Euler(0f, Quaternion.LookRotation(dir.normalized).eulerAngles.y, 0), .5f);
        }

        public void Idle() {
            this.stateMachine.SetState(this.idleState);
        }

        public void Interact(IInteractable interactable) {
            this.characterInteractState.Interactable = interactable;
            this.stateMachine.SetState(this.characterInteractState);
        }

        /// <summary>Entre dans l'état « occupé » (<see cref="PlayerState.BUSY"/>) le temps d'une
        /// action à UI bloquante (ATM, fouille). <paramref name="stillBusy"/> maintient l'état :
        /// dès qu'il devient faux (UI refermée), l'état se termine seul au Tick suivant (retour
        /// Idle). <paramref name="lookAt"/> oriente le joueur vers la cible (facultatif). À
        /// appeler AVANT d'ouvrir l'UI concernée (la sortie d'Idle referme l'inventaire).</summary>
        /// <param name="cancelOnMove">Une touche de déplacement ANNULE l'action (fouille…) :
        /// <paramref name="onCancelled"/> referme l'UI déclencheuse, puis retour Idle.</param>
        public void BeginBusy(Func<bool> stillBusy, Transform lookAt = null,
                              bool cancelOnMove = false, System.Action onCancelled = null) {
            if (this.stateMachine == null || this.busyState == null) return;
            this.busyState.Configure(stillBusy, lookAt, cancelOnMove, onCancelled);
            this.stateMachine.SetState(this.busyState);
        }

        public void Sit(ISeatBehavior props, Transform seatTransform) {
            this.stateMachine.SetState(new CharacterSit(this, props, seatTransform));
        }

        /// <summary>True tant que le joueur conduit un véhicule (état CharacterDrive).
        /// Géré exclusivement par CharacterDrive.OnEnter/OnExit ; consommé par CameraManager
        /// pour neutraliser le click-to-move pendant la conduite.</summary>
        public bool IsDriving { get; set; }

        /// <summary>Véhicule actuellement occupé (conducteur OU passager), sinon null. Posé/effacé
        /// par CharacterDrive/CharacterPassenger ; consommé par CameraManager pour autoriser le clic
        /// « Sortir » sur le véhicule occupé pendant que le reste de l'interaction est neutralisé.</summary>
        public VehicleController CurrentVehicle { get; set; }

        /// <summary>Entre dans l'état de conduite du véhicule (miroir de Sit()).</summary>
        public void DriveVehicle(VehicleController vehicle) {
            this.stateMachine.SetState(new CharacterDrive(this, vehicle));
        }

        /// <summary>True tant que le joueur est passager d'un véhicule (état CharacterPassenger).
        /// Comme IsDriving, sert à neutraliser le click-to-move / survol côté caméra.</summary>
        public bool IsPassenger { get; set; }

        /// <summary>Entre dans l'état passager (assis, vue véhicule, sortie via X).</summary>
        public void RidePassenger(VehicleController vehicle) {
            this.stateMachine.SetState(new CharacterPassenger(this, vehicle));
        }

        /// <summary>Enters the timed prop-construction state: looping anim + world progress
        /// bar above <paramref name="progressAnchor"/> (le prop) over <paramref name="duration"/> s.
        /// On completion fires <paramref name="onComplete"/>. Interrupted (cancelled) if any other
        /// state takes over before the timer ends. NE change PAS le StateType (caméra reste FREE).</summary>
        public void StartPropsBuilding(float duration, System.Action onComplete, System.Action onCancel = null) {
            this.stateMachine.SetState(new CharacterPropsBuilding(this, duration, onComplete, onCancel));
        }

        public void Sleep(ISeatBehavior props, Transform couchTransform) {
            this.stateMachine.SetState(new CharacterSleep(this, props, couchTransform));
        }

        public void Look(Transform target) {
            lookAtState.Target = target;
            this.stateMachine.SetState(lookAtState);
        }

        public void Die() {
            if (this.stateMachine == null) return;
            this.stateMachine.SetState(dieState);
        }

        // ── Renversement par véhicule (ragdoll) ──────────────────────────────────────

        /// <summary>Renverse le joueur (ragdoll) côté serveur : pose l'état répliqué, déclenche
        /// l'effondrement sur place sur tous les clients, programme la relève automatique. Pas de
        /// projection ni de dégâts (POC). Renvoie vrai si le renversement vient de démarrer (faux si
        /// déjà au sol ou mort) — l'appelant n'émet le son de choc qu'au renversement initial.</summary>
        [Server]
        public bool ServerKnockDown() {
            if (this.isKnockedDown) return false;                 // déjà au sol
            if (this._playerState == PlayerState.DIED) return false; // pas de renversement si mort
            this.isKnockedDown = true;
            this.RpcKnockdown();
            Invoke(nameof(ServerRecoverKnockdown), KnockdownDuration);
            return true;
        }

        [Server]
        private void ServerRecoverKnockdown() {
            this.isKnockedDown = false;
        }

        /// <summary>Déclenche l'effondrement ragdoll sur place, localement sur chaque client.</summary>
        [ClientRpc]
        private void RpcKnockdown() {
            if (this.ragdoll != null) this.ragdoll.EnableRagdoll();
        }

        /// <summary>Réplique l'entrée/sortie de ragdoll sur tous les clients. À l'entrée : ragdoll
        /// + (owner) verrouillage du déplacement + caméra owner réorientée sur les hanches (suit le
        /// corps pendant la chute). À la sortie : repos animé ; l'owner se relève sur place (racine
        /// repositionnée sur les hanches, échantillonnée sur le NavMesh) puis Idle, et la caméra
        /// revient sur la cible tête habituelle.</summary>
        private void OnKnockdownChanged(bool _, bool now) {
            if (now) {
                if (this.ragdoll != null) this.ragdoll.EnableRagdoll();
                if (isLocalPlayer) {
                    if (this.stateMachine != null) this.stateMachine.SetState(knockdownState);
                    // La caméra TPS suit les hanches du ragdoll via GetCameraFollowTarget().
                }
            } else {
                if (isLocalPlayer) {
                    Vector3 stand = this.ragdoll != null ? this.ragdoll.HipsPosition : transform.position;
                    if (UnityEngine.AI.NavMesh.SamplePosition(stand, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                        stand = hit.position;
                    if (this.ragdoll != null) this.ragdoll.DisableRagdoll();
                    transform.position = stand;
                    this.Idle();
                } else if (this.ragdoll != null) {
                    this.ragdoll.DisableRagdoll();
                }
            }
        }

        /// <summary>Réplique le ragdoll de MORT sur tous les clients. Purement visuel : l'état
        /// logique est dieState (Die côté serveur, TargetKill chez l'owner) — contrairement au
        /// knockdown, ce hook ne pose AUCUN état dans la state machine. À la relève (Revive),
        /// l'owner est redressé sur la position des hanches (le téléport domicile éventuel
        /// écrase ensuite la position ; un sans-abri se relève sur place).</summary>
        private void OnDeadChanged(bool _, bool now) {
            if (now) {
                if (this.ragdoll != null) this.ragdoll.EnableRagdoll();
                // Owner : caméra sur les hanches via GetCameraFollowTarget ; le verrouillage
                // des entrées vient de l'état DIED (dieState), pas d'ici.
            } else {
                if (isLocalPlayer) {
                    Vector3 stand = this.ragdoll != null ? this.ragdoll.HipsPosition : transform.position;
                    if (UnityEngine.AI.NavMesh.SamplePosition(stand, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                        stand = hit.position;
                    if (this.ragdoll != null) this.ragdoll.DisableRagdoll();
                    transform.position = stand;
                    // Le retour à Idle vient de TargetRevive (Invoke Idle), pas d'ici.
                } else if (this.ragdoll != null) {
                    this.ragdoll.DisableRagdoll();
                }
            }
        }

        [Server]
        public void Kill() {
            GameLogger.Player.Warning("PlayerKilled {PlayerNetId}", netId);
            // End any in-progress mission. Failing the job also despawns a held mission item
            // (via MissionItemCleanup) and clears the owner's mission UI (via OnMissionFinished).
            Sim.Missions.MissionServerManager.Instance.AbandonAllForOwner(this.netId);
            this.Die();
            // Lâche TOUT le butin du joueur dans un sac persistant au point de mort (déplace
            // mains/dos/poche dans la place du sac) AVANT le ragdoll — le sac vit 10 min
            // indépendamment du respawn.
            ServerItemManager.Instance?.DropPlayerLootBag(this);
            // Ragdoll de mort répliqué par le hook d'isDead (effondrement + caméra owner sur
            // les hanches). Un knockdown véhicule en cours est annulé proprement : l'état
            // logique de la mort est dieState seul.
            CancelInvoke(nameof(ServerRecoverKnockdown));
            this.isKnockedDown = false;
            this.isDead = true;
            this.TargetKill(this.netIdentity.connectionToClient);
            Invoke(nameof(Revive), DeathReviveDelay);
        }

        /// <summary>Respawn immédiat demandé par l'owner (bouton du HUD de mort). Idempotent :
        /// le garde isDead de Revive absorbe un double appel (bouton + auto-respawn).</summary>
        [Command]
        public void CmdRequestRespawn() {
            if (!this.isDead) return;
            CancelInvoke(nameof(Revive)); // annule le respawn auto programmé par Kill
            this.Revive();
        }

        /// <summary>Côté owner : demande de respawn immédiat (consommé par DeathScreenUI).</summary>
        [Client]
        public void RequestRespawn() {
            if (isLocalPlayer) this.CmdRequestRespawn();
        }

        [Server]
        public void Revive() {
            if (!this.isDead) return; // déjà relevé (bouton + Invoke auto : un seul passe)
            CancelInvoke(nameof(Revive));
            this.isDead = false; // relève du ragdoll de mort sur toutes les copies (hook)
            // NB : on NE purge PAS le sac de butin — il persiste au point de mort (10 min)
            // pour que le joueur puisse revenir récupérer ses affaires.
            this.playerHealth.ResetAll();
            this.playerBankAccount.PostLedger(-50, LedgerReason.DeathPenalty, LedgerCounterparty.System, LedgerCounterparty.Bank);

            // Homeless players (e.g. evicted) have no apartment to return to —
            // revive them in place (city) without an apartment teleport.
            BuildingBehavior buildingBehavior = this.characterHome?.Address != null
                ? FindObjectsByType<BuildingBehavior>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(x => x.Match(this.characterHome.Address))
                : null;

            if (buildingBehavior) {
                GameLogger.Player.Info("PlayerRevived {PlayerNetId} {BuildingStreet}", netId, this.characterHome.Address.street);
                buildingBehavior.TeleportExistingPlayerToApartment(this.characterHome.Address.doorNumber, this.netIdentity.connectionToClient);
            } else {
                GameLogger.Player.Info("PlayerRevivedHomeless {PlayerNetId}", netId);
            }

            this.TargetRevive(this.netIdentity.connectionToClient);
        }

        [TargetRpc]
        public void TargetRevive(NetworkConnection conn) {
            ClientLogger.Player("PlayerRevivedClient {PlayerNetId}", netId);
            Sim.UI.DeathScreenUI.Instance?.Hide();
            Invoke(nameof(Idle), 1f);
            NotificationManager.Instance.AddNotification("20 BC vous ont été volé lors de votre évanouissement. Les voleurs sont partout, faites attention à votre argent.", PhoneAppIds.Bank);
            ClientLogger.UI("ReviveNotificationShown");
        }

        [TargetRpc]
        public void TargetKill(NetworkConnection conn) {
            ClientLogger.Player("PlayerDiedClient {PlayerNetId}", netId);
            this.Die();
            // HUD de mort : compte à rebours du respawn auto + bouton de respawn immédiat.
            // La caméra passe en surplomb du cadavre (TpsShoulderCamera, vue de mort).
            Sim.UI.DeathScreenUI.Instance?.Show(DeathReviveDelay);
        }

        public IState CurrentState() {
            return this.stateMachine.CurrentState;
        }

        #endregion

        #region GETTERS / SETTERS

        public Transform GetHeadTargetForCamera() {
            return this.headTargetForCamera;
        }

        /// <summary>Point d'ancrage de la caméra TPS : la tête en temps normal, les hanches du
        /// ragdoll pendant un renversement OU la mort (la racine ne suit pas le corps qui
        /// s'effondre).</summary>
        public Transform GetCameraFollowTarget() {
            if ((this.isKnockedDown || this.isDead) && this.ragdoll != null && this.ragdoll.Hips != null) return this.ragdoll.Hips;
            return this.headTargetForCamera;
        }
        
        public void SetHeadTargetPosition(Vector3 localPosition) {
            this.headTargetForCamera.localPosition = localPosition;
        }

        public void SetState(StateType stateType) {
            Debug.Log($"Player state changed from {this.state} to {stateType}");
            this.state = stateType;
            OnStateChanged?.Invoke(this, stateType);
        }

        public void SetMood(MoodConfig moodConfig) {
            this.characterData.Mood = moodConfig.MoodEnum;
            this.animator.SetMood((int) moodConfig.MoodEnum);
            if (isLocalPlayer) OnCharacterDataChanged?.Invoke(this.characterData);
        }

        public StateType GetState() {
            return this.state;
        }

        public StateMachine StateMachine => stateMachine;

        public NavMeshAgent NavMeshAgent => navMeshAgent;

        /// <summary>Vitesse de déplacement à pied courante, quel que soit le mover actif
        /// (agent en FREE, CharacterController en TPS). Consommée par les UI qui se
        /// ferment dès que le joueur marche.</summary>
        public float CurrentMoveSpeed =>
            this.navMeshAgent != null && this.navMeshAgent.enabled
                ? this.navMeshAgent.velocity.magnitude
                : this.tpsCurrentSpeed;

        public Vector3 IdleHeadPosition => idleHeadPosition;

        public Vector3 SitHeadPosition => sitHeadPosition;

        public Vector3 SleepHeadPosition => sleepHeadPosition;

        public PlayerAnimator Animator => animator;

        public CharacterData CharacterData {
            get => characterData;
            set {
                characterData = value;
                this.animator.SetMood((int) characterData.Mood);
                if (isLocalPlayer) OnCharacterDataChanged?.Invoke(characterData);
            }
        }

        public Home CharacterHome => characterHome;

        public IInteractable InteractableTarget {
            get => interactableTarget;
            set => interactableTarget = value;
        }

        public Vector3 InteractionOriginPoint => interactionOriginPoint;

        public bool ShowRadialMenuWithPriority {
            get => showRadialMenuWithPriority;
            set => showRadialMenuWithPriority = value;
        }

        public IInteractable GetInteractedObject() => this.characterInteractState.Interactable;

        public Collider Collider { get; private set; }

        #endregion
    }
}