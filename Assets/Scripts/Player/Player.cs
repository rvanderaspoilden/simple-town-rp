using System;
using AI;
using AI.States;
using Photon.Pun;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using UnityEngine.AI;

namespace Sim {
    public class Player : MonoBehaviourPunCallbacks {
        [Header("Settings")] [SerializeField] private Transform headTargetForCamera;

        [Header("Only for debug")] [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField] private StateType state;

        [SerializeField] private Props propsTarget;

        private PlayerAnimator animator;

        private new Rigidbody rigidbody;

        private CharacterData characterData; // represent all database info relative to the character

        private StateMachine stateMachine;

        private CharacterIdle idleState;

        private CharacterMove moveState;

        public delegate void StateChanged(Player player, StateType state);

        public static event StateChanged OnStateChanged;

        public delegate void CharacterDataChanged(CharacterData characterData);

        public static event CharacterDataChanged OnCharacterDataChanged;

        private void Awake() {
            this.navMeshAgent = GetComponent<NavMeshAgent>();
            this.rigidbody = GetComponent<Rigidbody>();
            this.animator = GetComponent<PlayerAnimator>();

            PhotonNetwork.AddCallbackTarget(this);
        }

        private void Start() {
            if (!this.photonView.IsMine) {
                this.navMeshAgent.enabled = false;
                this.rigidbody.useGravity = false;
            }

            this.InitStateMachine();
        }

        private void OnDestroy() {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        private void Update() {
            if (!this.photonView.IsMine) return;

            this.stateMachine.Tick();
        }

        #region State Machine Management

        private void InitStateMachine() {
            this.stateMachine = new StateMachine();

            this.idleState = new CharacterIdle(this);
            this.moveState = new CharacterMove(this);

            this.stateMachine.AddTransition(moveState, idleState, HasReachedTargetPosition());

            this.stateMachine.SetState(idleState);
        }

        private Func<bool> HasReachedTargetPosition() => () => {
            return (this.propsTarget &&
                    this.navMeshAgent.remainingDistance > this.navMeshAgent.stoppingDistance &&
                    this.CanInteractWith(this.propsTarget)) ||
                   (!this.navMeshAgent.hasPath && MarkerController.Instance.IsActive());
        };

        #endregion

        #region ACTIONS
        
        public void SetTarget(Vector3 targetPoint, Props props) {
            this.navMeshAgent.SetDestination(targetPoint);
            this.propsTarget = props;
            this.stateMachine.SetState(moveState);
        }

        public void Sit(Transform seat) {
            this.navMeshAgent.enabled = false;
            this.transform.position = seat.position;
            this.transform.rotation = seat.rotation;
            this.animator.Sit();
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


        public NavMeshAgent NavMeshAgent => navMeshAgent;

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

            if (propsToInteract.GetActions()?.Length <= 0 || Mathf.Abs(Vector3.Distance(origin, target)) > maxRange) {
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

        #endregion
    }
}