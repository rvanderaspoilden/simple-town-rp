using System;
using System.Collections.Generic;
using System.Linq;
using AI;
using AI.States;
using DG.Tweening;
using Interaction;
using Mirror;
using Sim.Entities;
using Sim.Enums;
using Sim.Logging;
using Sim.Scriptables;
using Sim.UI;
using Sim.Utils;
using UnityEngine;
using UnityEngine.AI;
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

        [Header("Only for debug")]
        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private StateType state;

        [SerializeField]
        private IInteractable interactableTarget;

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

        [SyncVar]
        private PlayerState _playerState;

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

        public delegate void LocalPlayerMoveStarted();

        /// <summary>
        /// Fired once when the local player initiates a movement (MoveTo).
        /// UI flows tied to a current interaction (e.g. PropsContentUI) subscribe
        /// to this to close themselves cleanly when movement interrupts the interaction.
        /// </summary>
        public static event LocalPlayerMoveStarted OnLocalPlayerMoveStarted; // TODO: to remove and use state

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
        }

        public override void OnStopClient() {
            if (isLocalPlayer) {
                ClientLogger.Player("LocalPlayerStop {NetId}", netId);
                this.UnSubscribeActions(this.actions);
            }
        }

        public PlayerState PlayerState {
            get => _playerState;
            set => _playerState = value;
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

        [Server]
        public void SetRawCharacterHome(string data) {
            this.rawCharacterHome = data;
            this.characterHome = JsonUtility.FromJson<Home>(this.rawCharacterHome);
        }

        public void ParseCharacterData(string old, string newValue) {
            this.characterData = JsonUtility.FromJson<CharacterData>(newValue);
            this.characterStyleSetup.ApplyStyle(this.CharacterData.Style);
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

            if (!this.isTalking && Input.GetAxis("GlobalChat") != 0f) {
                this.CmdSetTalk(true);
                this.bubbleUI.SetVoiceBubbleVisibility(true);
            } else if (this.isTalking && Input.GetAxis("GlobalChat") == 0f) {
                this.CmdSetTalk(false);
                this.bubbleUI.SetVoiceBubbleVisibility(false);
            }
        }

        [Command]
        public void CmdSetTalk(bool value) {
            if (this.isTalking != value) {
                GameLogger.Network.Debug("CmdSetTalk {PlayerNetId} {Value}", netId, value);
                this.isTalking = value;
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
                    this.CanInteractWith(this.interactableTarget, this.interactableTarget.transform.position)) ||
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
            this.showRadialMenuWithPriority = showPriorityActions;
            MoveTo(targetPoint);
        }

        public void MoveTo(Vector3 targetPoint) {
            this.stateMachine.SetState(moveState);
            this.navMeshAgent.SetDestination(targetPoint);

            HUDManager.Instance.CloseInventory();

            if (isLocalPlayer)
            {
                Debug.Log("[Interaction] Player movement interrupted current interaction");
                OnLocalPlayerMoveStarted?.Invoke();
            }
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
            this.stateMachine.SetState(dieState);
        }

        [Server]
        public void Kill() {
            GameLogger.Player.Warning("PlayerKilled {PlayerNetId}", netId);
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

        public bool ShowRadialMenuWithPriority {
            get => showRadialMenuWithPriority;
            set => showRadialMenuWithPriority = value;
        }

        public IInteractable GetInteractedObject() => this.characterInteractState.Interactable;

        public Collider Collider { get; private set; }

        #endregion
    }
}