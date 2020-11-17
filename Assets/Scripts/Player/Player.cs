using System;
using System.Linq;
using Photon.Pun;
using Sim.Building;
using Sim.Entities;
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

        public delegate void StateChanged(StateType state);

        public static event StateChanged OnStateChanged;
        
        private void Awake() {
            this.agent = GetComponent<NavMeshAgent>();
            this.thirdPersonCharacter = GetComponent<ThirdPersonCharacter>();
            this.agent.updateRotation = false;

            PhotonNetwork.AddCallbackTarget(this);
        }

        private void OnDestroy() {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        private void Update() {
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
            photonView.RPC("RPC_SetTarget", RpcTarget.All, target);
        }

        [PunRPC]
        public void RPC_SetTarget(Vector3 target) {
            this.agent.SetDestination(target);
        }

        public Transform GetHeadTargetForCamera() {
            return this.headTargetForCamera;
        }

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) {
            photonView.RPC("RPC_UpdateStatus", newPlayer, this.transform.position, this.transform.rotation, this.agent.destination);
        }

        [PunRPC]
        public void RPC_UpdateStatus(Vector3 pos, Quaternion rotation, Vector3 target) {
            this.transform.position = pos;
            this.transform.rotation = rotation;
            this.agent.SetDestination(target);
        }
    }
}