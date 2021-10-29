using System;
using System.Collections.Generic;
using System.Linq;
using AI;
using AI.States;
using DG.Tweening;
using Mirror;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Scriptables;
using Sim.UI;
using UnityEngine;
using UnityEngine.AI;
using Action = Sim.Interactables.Action;
using Random = UnityEngine.Random;

namespace Sim {
    public class PlayerController : NetworkBehaviour {
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
        private Props propsTarget;

        [SerializeField]
        private bool showRadialMenuWithPriority;

        [SerializeField]
        private CharacterData characterData; // represent all database info relative to the character

        [SerializeField]
        private Home characterHome;

        [SyncVar]
        [SerializeField]
        private bool died;

        [SyncVar(hook = nameof(ParseCharacterData))]
        private string rawCharacterData;

        [SyncVar(hook = nameof(ParseCharacterHome))]
        private string rawCharacterHome;

        [SyncVar(hook = nameof(OnTalkingStateChanged))]
        private bool isTalking;

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
            }
        }

        public override void OnStartServer() {
            base.OnStartServer();

            if (!isClient) {
                this.navMeshAgent.enabled = false;
                this.rigidbody.useGravity = false;
                Destroy(GetComponent<AudioListener>());
            }
        }

        public override void OnStartLocalPlayer() {
            this.InitStateMachine();

            if (died) {
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
        }

        public override void OnStopClient() {
            if (isLocalPlayer) {
                this.UnSubscribeActions(this.actions);
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

        public void PlayStepSound() {
            this.audioSource.volume = 0.005f;
            this.audioSource.pitch = Random.Range(1f, 1.2f);
            this.audioSource.PlayOneShot(this.walkStepSound);
        }

        public void Consume(Consumable consumable) {
            this.CmdConsume(consumable.netId);
        }

        [Command]
        public void CmdConsume(uint itemNetId) {
            if (!NetworkIdentity.spawned.ContainsKey(itemNetId)) {
                Debug.LogError($"[PlayerController] [CmdEat] Cannot found netId [id={itemNetId}]");
            }

            Consumable consumable = NetworkIdentity.spawned[itemNetId].gameObject.GetComponent<Consumable>();

            this.playerHealth.ApplyModifications(((ConsumableConfig) consumable.Configuration).Impacts);

            NetworkServer.Destroy(consumable.gameObject);

            this.RpcConsume();
        }

        [ClientRpc]
        public void RpcConsume() {
            if (isLocalPlayer) {
                HUDManager.Instance.InventoryUI.Invoke(nameof(InventoryUI.UpdateUI), .1f);
            }

            this.audioSource.PlayOneShot(this.eatSound);
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
                    DefaultViewUI.Instance.SetTenantText($"Locataire: {current.GetComponentInParent<ApartmentController>().TenantIdentity.FullName}");
                } else {
                    DefaultViewUI.Instance.SetTenantText(string.Empty);
                }
            } else {
                DefaultViewUI.Instance.SetLocationText(string.Empty);
                DefaultViewUI.Instance.SetTenantText(string.Empty);
            }
        }

        public GeographicArea CurrentGeographicArea => currentGeographicArea.LastOrDefault();

        public PlayerHands PlayerHands => playerHands;

        [Server]
        public void SetRawCharacterData(string data) {
            this.rawCharacterData = data;
            this.characterData = JsonUtility.FromJson<CharacterData>(this.rawCharacterData);
            this.playerHealth.Init(this.characterData.Health);
            this.playerBankAccount.Init(this.characterData.Money);
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
            this.isTalking = value;
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
            return (this.propsTarget &&
                    this.navMeshAgent.remainingDistance > this.navMeshAgent.stoppingDistance &&
                    this.CanInteractWith(this.propsTarget)) ||
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

        public void SetTarget(Vector3 targetPoint, Props props, bool showPriorityActions = false) {
            this.propsTarget = props;
            this.showRadialMenuWithPriority = showPriorityActions;
            MoveTo(targetPoint);
        }

        public void MoveTo(Vector3 targetPoint) {
            this.stateMachine.SetState(moveState);
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

        public void Interact(Props propsToInteract) {
            this.characterInteractState.InteractedProps = propsToInteract;
            this.stateMachine.SetState(this.characterInteractState);
        }

        public void Sit(Seat props, Transform seatTransform) {
            this.stateMachine.SetState(new CharacterSit(this, props, seatTransform));
        }

        public void Sleep(Seat props, Transform couchTransform) {
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
            this.died = true;
            this.TargetKill(this.netIdentity.connectionToClient);
            Invoke(nameof(Revive), 4f);
        }

        [Server]
        public void Revive() {
            BuildingBehavior buildingBehavior = FindObjectsOfType<BuildingBehavior>().FirstOrDefault(x => x.Match(this.characterHome.Address));

            if (buildingBehavior) {
                buildingBehavior.TeleportToApartment(this.characterHome.Address.doorNumber, this.netIdentity.connectionToClient);
                this.playerHealth.ResetAll();
                this.died = false;
                this.playerBankAccount.TakeMoney(50);
                this.TargetRevive(this.netIdentity.connectionToClient);
            } else {
                Debug.LogError($"[PlayerController] [Revive] Cannot find building with street name {this.characterHome.Address.street}");
            }
        }

        [TargetRpc]
        public void TargetRevive(NetworkConnection conn) {
            Debug.Log("I'm now alive");
            Invoke(nameof(Idle), 1f);
            NotificationUI.Instance.Show("Vous êtes tombé inconscient.\n Faites attention à votre santé !\n ps: On vous a volé...");
        }

        [TargetRpc]
        public void TargetKill(NetworkConnection conn) {
            Debug.Log("You are died");
            this.Die();
        }

        [Client]
        public void Sell(Props props) {
            CmdSell(props.netId);
        }

        [Command]
        public void CmdSell(uint propsNetId) {
            if (!NetworkIdentity.spawned.ContainsKey(propsNetId)) {
                Debug.LogError($"Server: Try to sell {propsNetId} but it not exist");
            }

            GameObject propsObject = NetworkIdentity.spawned[propsNetId].gameObject;

            Debug.Log($"Server: player {netId} sold {propsObject.name}");

            propsObject.SetActive(false);

            NetworkServer.Destroy(propsObject);

            StartCoroutine(propsObject.GetComponentInParent<ApartmentController>().Save());
        }

        public IState CurrentState() {
            return this.stateMachine.CurrentState;
        }

        #endregion

        #region GETTERS / SETTERS

        public Transform GetHeadTargetForCamera() {
            return this.headTargetForCamera;
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

        public Props PropsTarget {
            get => propsTarget;
            set => propsTarget = value;
        }

        public bool ShowRadialMenuWithPriority {
            get => showRadialMenuWithPriority;
            set => showRadialMenuWithPriority = value;
        }

        public Props GetInteractedProps() => this.characterInteractState.InteractedProps;

        public Collider Collider { get; private set; }

        public bool Died => died;

        #endregion

        #region Utility

        public bool CanInteractWith(Props propsToInteract) {
            float maxRange = propsToInteract.GetConfiguration().GetRangeToInteract();
            Vector3 origin = Vector3.Scale(propsToInteract.transform.position, new Vector3(1, 0, 1));
            Vector3 target = Vector3.Scale(this.transform.position, new Vector3(1, 0, 1));

            if (propsToInteract.GetActions()?.Length <= 0 || Mathf.Abs(Vector3.Distance(origin, target)) > maxRange) {
                return false;
            }

            Vector3 dir = propsToInteract.transform.position - this.GetHeadTargetForCamera().position;
            RaycastHit hit;
            if (Physics.Raycast(this.GetHeadTargetForCamera().position, dir, out hit)) {
                Props hitProps = hit.collider.GetComponentInParent<Props>();

                if ((hitProps && hitProps.GetType() == typeof(Wall)) || !hitProps) {
                    return false;
                }

                return hitProps.Equals(propsToInteract);
            }

            return false;
        }

        public bool CanInteractWith(Props propsToInteract, Vector3 hitPoint) {
            float maxRange = propsToInteract.GetConfiguration().GetRangeToInteract();
            Vector3 origin = Vector3.Scale(hitPoint, new Vector3(1, 0, 1));
            Vector3 target = Vector3.Scale(this.transform.position, new Vector3(1, 0, 1));

            if (Mathf.Abs(Vector3.Distance(origin, target)) > maxRange) {
                return false;
            }

            Vector3 dir = hitPoint - this.GetHeadTargetForCamera().position;
            RaycastHit hit;
            if (Physics.Raycast(this.GetHeadTargetForCamera().position, dir, out hit)) {
                Props hitProps = hit.collider.GetComponentInParent<Props>();

                if ((hitProps && hitProps.GetType() == typeof(Wall)) || !hitProps) {
                    return false;
                }

                return hitProps.Equals(propsToInteract);
            }

            return false;
        }

        public bool CanInteractWith(Item itemToInteract, Vector3 hitPoint) {
            Vector3 origin = Vector3.Scale(hitPoint, new Vector3(1, 0, 1));
            Vector3 target = Vector3.Scale(this.transform.position, new Vector3(1, 0, 1));

            if (Mathf.Abs(Vector3.Distance(origin, target)) > 1f) {
                return false;
            }

            Vector3 dir = hitPoint - this.GetHeadTargetForCamera().position;
            RaycastHit hit;

            if (Physics.Raycast(this.GetHeadTargetForCamera().position, dir, out hit)) {
                Item hitItem = hit.collider.GetComponentInParent<Item>();

                return hitItem && hitItem.Equals(itemToInteract);
            }

            return false;
        }

        public void SetHeadTargetPosition(Vector3 localPosition) {
            this.headTargetForCamera.localPosition = localPosition;
        }

        #endregion
    }
}