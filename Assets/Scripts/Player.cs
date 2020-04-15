using System;
using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Sim {
    public class Player : MonoBehaviourPunCallbacks {
        [Header("Settings")]
        [SerializeField] private Transform headTargetForCamera;

        [Header("Only for debug")]
        [SerializeField] private NavMeshAgent agent;

        [SerializeField] private ThirdPersonCharacter thirdPersonCharacter;

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