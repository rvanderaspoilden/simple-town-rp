using System;
using System.Linq;
using Photon.Pun;
using Sim.Building;
using Sim.Enums;
using UnityEngine;
using UnityEngine.AI;

namespace Sim {
    public class Player : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField] private Transform headTargetForCamera;

        [Header("Only for debug")]
        [SerializeField] private NavMeshAgent agent;

        [SerializeField] private ThirdPersonCharacter thirdPersonCharacter;
        
        [SerializeField]
        private StateType state;

        private new Rigidbody rigidbody;

        public delegate void StateChanged(StateType state);

        public static event StateChanged OnStateChanged;
        
        private void Awake() {
            this.agent = GetComponent<NavMeshAgent>();
            this.thirdPersonCharacter = GetComponent<ThirdPersonCharacter>();
            this.agent.updateRotation = false;
            this.rigidbody = GetComponent<Rigidbody>();

            PhotonNetwork.AddCallbackTarget(this);
        }

        private void Start() {
            if (!this.photonView.IsMine) {
                this.agent.enabled = false;
                this.thirdPersonCharacter.enabled = false;
                this.rigidbody.useGravity = false;
            }
        }

        private void OnDestroy() {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        private void Update() {
            if (!this.photonView.IsMine) return;
            
            thirdPersonCharacter.Move(this.agent.remainingDistance > this.agent.stoppingDistance ? this.agent.desiredVelocity : Vector3.zero, false, false);
        }

        public bool CanInteractWith(Props propsToInteract) {
            // TODO: refactor this because range is shit
            return propsToInteract.GetActions()?.Length > 0 && Physics.OverlapSphere(this.GetHeadTargetForCamera().position, propsToInteract.GetConfiguration().GetRangeToInteract()).ToList().Where(collider => collider.gameObject == propsToInteract.gameObject).ToList().Count == 1;
        }
        
        public void SetState(StateType stateType) {
            Debug.Log($"Player state changed from {this.state} to {stateType}");
            this.state = stateType;
            OnStateChanged?.Invoke(stateType);
        }

        public StateType GetState() {
            return this.state;
        }

        public void SetTarget(Vector3 target) {
            this.agent.SetDestination(target);
        }

        public Transform GetHeadTargetForCamera() {
            return this.headTargetForCamera;
        }
    }
}