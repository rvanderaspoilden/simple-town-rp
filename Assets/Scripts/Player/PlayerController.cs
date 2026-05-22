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
using Sim.Enums;
using Sim.Jobs;
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

        private CharacterLookAt lookAtState;

        private CharacterInteract characterInteractState;

        private CharacterDie dieState;

        private CharacterStyleSetup characterStyleSetup;

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

            Sim.Jobs.JobTargetHooks.RegisterPlayer(this);
        }

        public override void OnStopServer() {
            Sim.Jobs.JobTargetHooks.UnregisterPlayer(this);
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

        public void ConsumeItem(int entityId) {
            this.CmdConsumeItem(entityId);
        }

        [Command]
        public void CmdConsumeItem(int entityId) {
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
            this.RpcConsume();
        }

        [ClientRpc]
        public void RpcConsume() {
            ClientLogger.NetworkDebug("RpcConsume {PlayerNetId} {IsLocalPlayer}", netId, isLocalPlayer);
            
            if (isLocalPlayer) {
                HUDManager.Instance.InventoryUI.Invoke(nameof(InventoryUI.UpdateUI), .1f);
                ClientLogger.UI("InventoryUpdateTriggered");
            }
            
            this.audioSource.PlayOneShot(this.eatSound);
            ClientLogger.Audio("EatSoundPlayed {PlayerNetId}", netId);
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
        /// Server-only career change. newJob=-1 means resign (currentJob → null).
        /// Persists to backend (start_or_resume + update_current_job) then
        /// rebroadcasts CharacterData. Active mission, if any, is abandoned.
        /// </summary>
        [Server]
        public void StartCareerChange(int newJob) {
            StartCoroutine(CareerChangeCoroutine(newJob));
        }

        [Server]
        private IEnumerator CareerChangeCoroutine(int newJob) {
            if (characterData == null || string.IsNullOrEmpty(characterData.Id)) {
                GameLogger.Network.Warning("CareerChangeSkipped_NoCharacter {NetId}", netId);
                yield break;
            }

            // Abandon any in-flight job; rewards system / cleanup handle the rest.
            JobServerManager.Instance.OnPlayerDisconnected(netId);

            // Upsert the destination row when applying to a job (skip on resign).
            if (newJob >= 0) {
                var startBody = new CharacterJobStartRequest {
                    characterId = characterData.Id,
                    category = newJob,
                };
                UnityWebRequest startReq = ApiManager.Instance.StartCharacterJobRequest(startBody);
                yield return startReq.SendWebRequest();
                if (startReq.responseCode != 200 && startReq.responseCode != 201) {
                    GameLogger.Network.Error(null, "CareerStartFailed {CharacterId} {Category} {Code}",
                        characterData.Id, newJob, startReq.responseCode);
                    yield break;
                }
                CharacterJobData row = JsonUtility.FromJson<CharacterJobData>(startReq.downloadHandler.text);
                if (row != null) MergeJob(row);
            }

            var updateBody = new CharacterUpdateCurrentJobRequest { currentJob = newJob };
            UnityWebRequest updateReq = ApiManager.Instance.UpdateCharacterCurrentJobRequest(characterData.Id, updateBody);
            yield return updateReq.SendWebRequest();
            if (updateReq.responseCode != 200) {
                GameLogger.Network.Error(null, "CareerUpdateCurrentJobFailed {CharacterId} {NewJob} {Code}",
                    characterData.Id, newJob, updateReq.responseCode);
                yield break;
            }

            characterData.CurrentJobRaw = newJob;
            SetRawCharacterData(JsonUtility.ToJson(characterData));
        }

        [Server]
        private void MergeJob(CharacterJobData row) {
            var list = characterData.Jobs;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].Category == row.Category) {
                    list[i] = row;
                    return;
                }
            }
            list.Add(row);
        }

        /// <summary>
        /// Server-only XP bump. Increments xp on the CharacterJob row for the
        /// given category (creating it if missing), rebroadcasts CharacterData,
        /// and persists via PUT /character-jobs/add-xp.
        /// </summary>
        [Server]
        public void AddJobXp(int category, int delta) {
            if (delta == 0 || characterData == null || string.IsNullOrEmpty(characterData.Id)) return;

            CharacterJobData row = null;
            var list = characterData.Jobs;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].Category == category) { row = list[i]; break; }
            }
            if (row == null) {
                row = new CharacterJobData { Category = category };
                list.Add(row);
            }
            row.Xp += delta;

            SetRawCharacterData(JsonUtility.ToJson(characterData));
            StartCoroutine(PersistJobXp(category, delta));
        }

        [Server]
        private IEnumerator PersistJobXp(int category, int delta) {
            var body = new CharacterJobAddXpRequest {
                characterId = characterData.Id,
                category = category,
                delta = delta,
            };
            UnityWebRequest req = ApiManager.Instance.AddCharacterJobXpRequest(body);
            yield return req.SendWebRequest();
            if (req.responseCode != 200) {
                GameLogger.Network.Error(null, "JobXpPersistFailed {CharacterId} {Category} {Delta} {Code}",
                    characterData.Id, category, delta, req.responseCode);
            }
        }

        [Server]
        public void SetRawCharacterHome(string data) {
            this.rawCharacterHome = data;
            this.characterHome = JsonUtility.FromJson<Home>(this.rawCharacterHome);
        }

        public void ParseCharacterData(string old, string newValue) {
            this.characterData = JsonUtility.FromJson<CharacterData>(newValue);
            this.characterStyleSetup.ApplyStyle(this.CharacterData.Style);
            OnCharacterDataChanged?.Invoke(this.characterData);
        }

        public string RawCharacterHome {
            get => rawCharacterHome;
            set => rawCharacterHome = value;
        }

        public void ParseCharacterHome(string old, string newValue) {
            this.characterHome = JsonUtility.FromJson<Home>(newValue);
            Debug.Log("TOTO");
        }

        private void Update() {
            if (!isLocalPlayer || this.stateMachine == null) return;

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
            this.lookAtState = new CharacterLookAt(this);
            this.characterInteractState = new CharacterInteract(this);
            this.dieState = new CharacterDie(this);

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

            return (this.interactableTarget != null &&
                    this.navMeshAgent.remainingDistance > this.navMeshAgent.stoppingDistance &&
                    this.CanInteractWith(this.interactableTarget, this.interactionOriginPoint)) ||
                   (!this.navMeshAgent.hasPath && MarkerController.Instance.IsActive());
        };

        private Func<bool> HasLostTarget() => () => this.lookAtState.Target == null;

        #endregion

        #region ACTIONS

        public Action[] Actions => actions;

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
            }
        }

        public void SetTarget(Vector3 targetPoint, IInteractable interactable, bool showPriorityActions = false) {
            string targetName = (interactable as UnityEngine.Object)?.name ?? interactable?.GetType().Name ?? "null";
            Debug.Log($"[Interaction] Started interaction target={targetName}");
            this.interactableTarget = interactable;
            this.interactionOriginPoint = targetPoint;
            this.showRadialMenuWithPriority = showPriorityActions;
            MoveTo(targetPoint);
        }

        public void MoveTo(Vector3 targetPoint, bool isRunning = false) {
            this.stateMachine.SetState(moveState);
            this.navMeshAgent.speed = isRunning ? runSpeed : walkSpeed;
            this.navMeshAgent.SetDestination(targetPoint);

            HUDManager.Instance.CloseInventory();
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

        [Server]
        public void Kill() {
            GameLogger.Player.Warning("PlayerKilled {PlayerNetId}", netId);
            // End any in-progress mission. Failing the job also despawns a held mission item
            // (via JobItemCleanup) and clears the owner's mission UI (via OnJobFinished).
            Sim.Jobs.JobServerManager.Instance.AbandonAllForOwner(this.netId);
            this.Die();
            this.TargetKill(this.netIdentity.connectionToClient);
            Invoke(nameof(Revive), 4f);
        }

        [Server]
        public void Revive() {
            BuildingBehavior buildingBehavior = FindObjectsByType<BuildingBehavior>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Match(this.characterHome.Address));

            if (buildingBehavior) {
                GameLogger.Player.Info("PlayerRevived {PlayerNetId} {BuildingStreet}", netId, this.characterHome.Address.street);
                buildingBehavior.TeleportExistingPlayerToApartment(this.characterHome.Address.doorNumber, this.netIdentity.connectionToClient);
                this.playerHealth.ResetAll();
                this.playerBankAccount.TakeMoney(50);
                this.TargetRevive(this.netIdentity.connectionToClient);
            } else {
                GameLogger.Network.Error(null, "ReviveBuildingNotFound {PlayerNetId} {Street}", netId, this.characterHome.Address.street);
            }
        }

        [TargetRpc]
        public void TargetRevive(NetworkConnection conn) {
            ClientLogger.Player("PlayerRevivedClient {PlayerNetId}", netId);
            Invoke(nameof(Idle), 1f);
            NotificationManager.Instance.AddNotification("20 BC vous ont été volé lors de votre évanouissement. Les voleurs sont partout, faites attention à votre argent.", NotificationType.BANK);
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
            OnCharacterDataChanged?.Invoke(this.characterData);
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
                OnCharacterDataChanged?.Invoke(characterData);
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