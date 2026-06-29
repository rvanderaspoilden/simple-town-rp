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
        [SerializeField] private float walkSpeed = 1.5f;
        [Tooltip("Vitesse de course (m/s) — appliquée quand MoveTo est appelée avec isRunning=true (double clic sur le sol).")]
        [SerializeField] private float runSpeed = 3.0f;

        [Header("Keyboard movement (experimental)")]
        [Tooltip("Active le déplacement clavier direct (ZQSD) relatif caméra, en plus du click-to-move.")]
        [SerializeField] private bool keyboardMovementEnabled = true;
        [Tooltip("Vitesse de rotation (deg/s) du personnage vers la direction de déplacement clavier.")]
        [SerializeField] private float freeMoveTurnSpeed = 720f;
        [SerializeField] private KeyCode forwardKey = KeyCode.Z;
        [SerializeField] private KeyCode backKey = KeyCode.S;
        [SerializeField] private KeyCode leftKey = KeyCode.Q;
        [SerializeField] private KeyCode rightKey = KeyCode.D;
        [SerializeField] private KeyCode runKey = KeyCode.LeftShift;

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

        /// <summary>Le joueur est-il actuellement renversé ? Consommé par CameraManager pour
        /// neutraliser le click-to-move / l'interaction (comme IsDriving).</summary>
        public bool IsKnockedDown => isKnockedDown;

        private const float KnockdownDuration = 3f;

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
            this.rigidbody = GetComponent<Rigidbody>();
            this.animator = GetComponent<PlayerAnimator>();
            this.playerHands = GetComponent<PlayerHands>();
            this.playerHealth = GetComponent<PlayerHealth>();
            this.playerBankAccount = GetComponent<PlayerBankAccount>();
            this.Collider = GetComponent<Collider>();
            this.characterStyleSetup = GetComponent<CharacterStyleSetup>();
            this.ragdoll = GetComponent<RagdollController>();
            this._navPathScratch = new NavMeshPath();
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

            if (this._playerState == PlayerState.DIED) {
                this.stateMachine.SetState(dieState);
            } else {
                this.stateMachine.SetState(idleState);
            }

            CameraManager.Instance.SetCameraTarget(this.GetHeadTargetForCamera());
            this.navMeshAgent.updateRotation = false;
            Local = this;
            HUDManager.Instance.DisplayPanel(PanelTypeEnum.DEFAULT);
            CharacterInfoPanelUI.Instance.Setup(this.characterData);
            CharacterInfoPanelUI.Instance.Setup(this.characterHome);
            CharacterInfoPanelUI.Instance.UpdateHealthUI(this.playerHealth.Health);
            CharacterInfoPanelUI.Instance.UpdateMoney(this.playerBankAccount.Money);

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

                if (current.Type == GeographicType.APARTMENT) {
                    DefaultViewUI.Instance.SetTenantText($"Locataire: {current.GetComponentInParent<ApartmentController>().TenantFullName}");
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
            if (isLocalPlayer && CharacterInfoPanelUI.Instance != null) {
                CharacterInfoPanelUI.Instance.Setup(this.characterHome);
            }
        }

        private void Update() {
            if (!isLocalPlayer || this.stateMachine == null) return;

            this.HandleKeyboardMovementEntry();

            this.stateMachine.Tick();

            // Voice push-to-talk indicator: the talking bubble follows the same "V" key
            // used by VoiceRoomAdapter to transmit voice.
            bool talkKeyHeld = Input.GetKey(KeyCode.V);
            if (!this.isTalking && talkKeyHeld) {
                this.CmdSetTalk(true);
                this.bubbleUI.SetVoiceBubbleVisibility(true);
            } else if (this.isTalking && !talkKeyHeld) {
                this.CmdSetTalk(false);
                this.bubbleUI.SetVoiceBubbleVisibility(false);
            }

            // T opens the text-chat panel — but not while the player is typing in a text
            // field (chat, shop search, price input…), otherwise typing "t" would open chat.
            if (Input.GetKeyDown(KeyCode.T) && !IsTypingInInputField() && HUDManager.Instance != null) {
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

        public void SetTarget(Vector3 targetPoint, IInteractable interactable, bool showPriorityActions = false, bool isRunning = false) {
            string targetName = (interactable as UnityEngine.Object)?.name ?? interactable?.GetType().Name ?? "null";
            Debug.Log($"[Interaction] Started interaction target={targetName}");
            this.interactableTarget = interactable;
            this.interactionOriginPoint = targetPoint;
            this.showRadialMenuWithPriority = showPriorityActions;
            // Interaction : la cible est la surface d'un prop (souvent hors NavMesh) → on autorise un
            // chemin partiel pour marcher au plus près, la portée étant ensuite validée par CanInteractWith.
            MoveTo(targetPoint, isRunning, allowPartialPath: true);
        }

        /// <param name="allowPartialPath">Pour une INTERACTION : le point visé est la surface d'un prop,
        /// souvent HORS NavMesh → la garde de chemin est sautée et on laisse SetDestination snapper sur la
        /// NavMesh la plus proche et marcher au plus près (la portée réelle est validée ensuite par
        /// CanInteractWith). Pour un clic au sol on garde false (chemin COMPLET exigé, sinon le joueur
        /// partirait vers un point inaccessible).</param>
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

        // ── Déplacement clavier direct (ZQSD) — expérimental ─────────────────────────

        /// <summary>Lit l'axe directionnel ZQSD brut : x = D(droite) − Q(gauche), y = Z(avant) − S(arrière).</summary>
        public Vector2 ReadMoveAxis() {
            float x = 0f, y = 0f;
            if (Input.GetKey(forwardKey)) y += 1f;
            if (Input.GetKey(backKey)) y -= 1f;
            if (Input.GetKey(rightKey)) x += 1f;
            if (Input.GetKey(leftKey)) x -= 1f;
            return new Vector2(x, y);
        }

        /// <summary>Engage l'état de déplacement clavier direct dès qu'un input ZQSD est détecté,
        /// uniquement depuis un état « libre de marcher » (idle / click-to-move). Coexiste avec le
        /// click-to-move ; neutralisé pendant la frappe UI, le knockdown, la conduite et la mort.</summary>
        private void HandleKeyboardMovementEntry() {
            if (!keyboardMovementEnabled) return;
            if (this.stateMachine.CurrentState == freeMoveState) return; // déjà en déplacement clavier
            if (IsTypingInInputField()) return;
            if (this.isKnockedDown || this.IsDriving || this.IsPassenger) return;
            if (this._playerState == PlayerState.DIED) return;

            // Symétrique au click-to-move (CameraManager n'appelle ManageInteraction qu'en mode FREE) :
            // le clavier ne doit pas faire bouger le joueur en BUILD/DRIVE/FPS.
            if (CameraManager.Instance != null && CameraManager.Instance.GetMode() != CameraModeEnum.FREE) return;

            IState current = this.stateMachine.CurrentState;
            if (current != idleState && current != moveState) return; // pas depuis sit/sleep/interact/...

            if (this.ReadMoveAxis().sqrMagnitude > 0.01f) {
                this.stateMachine.SetState(freeMoveState);
            }
        }

        /// <summary>Pilote le personnage en ZQSD relatif à la caméra via NavMeshAgent.Move (contraint à
        /// la NavMesh). Renvoie false en l'absence d'input directionnel — l'état CharacterFreeMove
        /// retombe alors sur idle. Course avec la touche dédiée (Maj). Owner local uniquement.</summary>
        public bool TickFreeMove() {
            // Sortie immédiate si le mode caméra a quitté FREE en cours de déplacement (entrée
            // build, passager, FPS) : on retombe en idle pour ne pas continuer à driver l'agent.
            if (CameraManager.Instance != null && CameraManager.Instance.GetMode() != CameraModeEnum.FREE) {
                this.animator.SetVelocity(0f);
                return false;
            }

            Vector2 axis = this.ReadMoveAxis();
            if (axis.sqrMagnitude < 0.01f) {
                this.animator.SetVelocity(0f);
                return false;
            }

            // Base caméra aplatie sur le plan horizontal : ZQSD est relatif à l'orientation de la vue.
            Transform cam = CameraManager.Instance != null ? CameraManager.Instance.Camera.transform : null;
            Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = cam != null ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;

            Vector3 dir = forward * axis.y + right * axis.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            if (dir.sqrMagnitude < 0.0001f) {
                this.animator.SetVelocity(0f);
                return false;
            }

            bool running = Input.GetKey(runKey);
            float speed = (running ? runSpeed : walkSpeed) * MoveSpeedPerkMultiplier();

            if (this.navMeshAgent != null && this.navMeshAgent.enabled && this.navMeshAgent.isOnNavMesh) {
                this.navMeshAgent.Move(dir * speed * Time.deltaTime);
            }

            Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            this.transform.rotation = Quaternion.RotateTowards(this.transform.rotation, targetRot, freeMoveTurnSpeed * Time.deltaTime);

            this.animator.SetVelocity(speed);
            return true;
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

        /// <summary>Démarre la « pose » d'un item tenu : le personnage marche jusqu'à
        /// <paramref name="standPoint"/> (point accessible le plus proche de l'emplacement choisi),
        /// puis à l'arrivée déclenche <paramref name="onArrive"/> (envoi de la requête de pose).
        /// Interrompu (annulé via <paramref name="onCancel"/>) si un autre state prend la main avant
        /// l'arrivée (clic-déplacement, interaction…). Renvoie faux si aucun chemin complet n'existe
        /// vers <paramref name="standPoint"/> (l'appelant garde alors l'item en main et notifie).</summary>
        public bool StartItemPose(Vector3 standPoint, System.Action onArrive, System.Action onCancel = null) {
            if (this.navMeshAgent == null || !this.navMeshAgent.enabled || !this.navMeshAgent.isOnNavMesh) return false;

            // Même garde que MoveTo : on n'engage la marche que si un chemin COMPLET existe (sinon
            // le personnage resterait figé sans jamais « arriver », bloquant la pose pour toujours).
            if (!this.navMeshAgent.CalculatePath(standPoint, _navPathScratch)
                || _navPathScratch.status != NavMeshPathStatus.PathComplete) {
                return false;
            }

            this.navMeshAgent.speed = walkSpeed * MoveSpeedPerkMultiplier();
            this.stateMachine.SetState(new CharacterPoser(this, standPoint, onArrive, onCancel));
            return true;
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
                    if (this.ragdoll != null && this.ragdoll.Hips != null)
                        CameraManager.Instance.SetCameraTarget(this.ragdoll.Hips);
                }
            } else {
                if (isLocalPlayer) {
                    Vector3 stand = this.ragdoll != null ? this.ragdoll.HipsPosition : transform.position;
                    if (UnityEngine.AI.NavMesh.SamplePosition(stand, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                        stand = hit.position;
                    CameraManager.Instance.SetCameraTarget(this.GetHeadTargetForCamera());
                    if (this.ragdoll != null) this.ragdoll.DisableRagdoll();
                    transform.position = stand;
                    this.Idle();
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
            this.TargetKill(this.netIdentity.connectionToClient);
            Invoke(nameof(Revive), 4f);
        }

        [Server]
        public void Revive() {
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
            Invoke(nameof(Idle), 1f);
            NotificationManager.Instance.AddNotification("20 BC vous ont été volé lors de votre évanouissement. Les voleurs sont partout, faites attention à votre argent.", PhoneAppIds.Bank);
            ClientLogger.UI("ReviveNotificationShown");
        }

        [TargetRpc]
        public void TargetKill(NetworkConnection conn) {
            ClientLogger.Player("PlayerDiedClient {PlayerNetId}", netId);
            this.Die();
        }

        public IState CurrentState() {
            return this.stateMachine.CurrentState;
        }

        #endregion

        #region GETTERS / SETTERS

        public Transform GetHeadTargetForCamera() {
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