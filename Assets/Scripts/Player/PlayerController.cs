﻿using System;
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

        [SyncVar(hook = nameof(ParseCharacterData))]
        private string rawCharacterData;

        private PlayerAnimator animator;

        private new Rigidbody rigidbody;

        private StateMachine stateMachine;

        private CharacterIdle idleState;

        private CharacterMove moveState;

        private CharacterLookAt lookAtState;

        private CharacterInteract characterInteractState;

        public delegate void StateChanged(PlayerController player, StateType state);

        public static event StateChanged OnStateChanged;

        public delegate void CharacterDataChanged(CharacterData characterData);

        public static event CharacterDataChanged OnCharacterDataChanged;

        public static PlayerController Local;

        private void Awake() {
            this.navMeshAgent = GetComponent<NavMeshAgent>();
            this.rigidbody = GetComponent<Rigidbody>();
            this.animator = GetComponent<PlayerAnimator>();
            this.Collider = GetComponent<Collider>();
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
            CameraManager.Instance.SetCameraTarget(this.GetHeadTargetForCamera());
            this.navMeshAgent.updateRotation = false;
            Local = this;
            HUDManager.Instance.DisplayPanel(PanelTypeEnum.DEFAULT);
            CharacterInfoPanelUI.Instance.Setup(this.characterData);
        }

        public override void OnStopClient() {
            if (isLocalPlayer) {
                this.UnSubscribeActions(this.actions);
            }
        }

        public string RawCharacterData {
            get => rawCharacterData;
            set => rawCharacterData = value;
        }

        public void ParseCharacterData(string old, string newValue) {
            this.characterData = JsonUtility.FromJson<CharacterData>(newValue);
        }

        private void Update() {
            if (!isLocalPlayer || this.stateMachine == null) return;

            this.stateMachine.Tick();
        }

        #region State Machine Management

        private void InitStateMachine() {
            this.stateMachine = new StateMachine();

            this.idleState = new CharacterIdle(this);
            this.moveState = new CharacterMove(this);
            this.lookAtState = new CharacterLookAt(this);
            this.characterInteractState = new CharacterInteract(this);

            this.stateMachine.AddTransition(moveState, idleState, HasReachedTargetPosition());
            this.stateMachine.AddTransition(lookAtState, idleState, HasLostTarget());

            this.stateMachine.SetState(idleState);
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

        public void SetHeadTargetPosition(Vector3 localPosition) {
            this.headTargetForCamera.localPosition = localPosition;
        }

        #endregion
    }
}