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
        [SerializeField]
        private Transform headTargetForCamera;

        [Header("Only for debug")]
        [SerializeField]
        private NavMeshAgent agent;

        [SerializeField]
        private ThirdPersonCharacter thirdPersonCharacter;

        [SerializeField]
        private StateType state;

        private new Rigidbody rigidbody;

        public delegate void StateChanged(Player player, StateType state);

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

            if (this.agent.remainingDistance > this.agent.stoppingDistance) {
                MarkerController.Instance.ShowAt(this.agent.pathEndPosition);
            } else if(MarkerController.Instance.IsActive()){
                MarkerController.Instance.Hide();
            }

            thirdPersonCharacter.Move(this.agent.remainingDistance > this.agent.stoppingDistance ? this.agent.desiredVelocity : Vector3.zero, false, false);
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

        public void SetState(StateType stateType) {
            Debug.Log($"Player state changed from {this.state} to {stateType}");
            this.state = stateType;
            OnStateChanged?.Invoke(this, stateType);
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