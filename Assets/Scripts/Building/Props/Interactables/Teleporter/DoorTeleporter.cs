using System;
using System.Collections;
using Photon.Pun;
using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public class DoorTeleporter : Teleporter {
        [Header("Door Settings")]
        [SerializeField] private DoorDirectionEnum doorDirection = DoorDirectionEnum.FORWARD;

        private Animator animator;
        private Coroutine doorAnimationCoroutine;

        private void Awake() {
            base.Awake();
            
            this.animator = GetComponent<Animator>();
            this.animator.SetFloat("direction", doorDirection == DoorDirectionEnum.BACKWARD ? -1 : 1);
        }

        private void OnDestroy() {
            StopAllCoroutines();
        }

        public override void Use() {
            // Play open animation for all
            photonView.RPC("RPC_Animation", RpcTarget.All);
            
            base.Use();
        }

        public override void Synchronize(Photon.Realtime.Player playerTarget) {
            base.Synchronize(playerTarget);
            
            this.SetDoorDirection(this.doorDirection, playerTarget);
        }

        public DoorDirectionEnum GetDoorDirection() {
            return this.doorDirection;
        }

        public void SetDoorDirection(DoorDirectionEnum doorDirectionEnum, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetDoorDirection", rpcTarget, doorDirectionEnum);
        }
        
        public void SetDoorDirection(DoorDirectionEnum doorDirectionEnum, Photon.Realtime.Player playerTarget) {
            photonView.RPC("RPC_SetDoorDirection", playerTarget, doorDirectionEnum);
        }

        [PunRPC]
        public void RPC_SetDoorDirection(DoorDirectionEnum doorDirectionEnum) {
            this.doorDirection = doorDirectionEnum;
            this.animator.SetFloat("direction", doorDirection == DoorDirectionEnum.BACKWARD ? -1 : 1);
        }

        [PunRPC]
        public void RPC_Animation() {
            if (this.doorAnimationCoroutine != null) {
                StopCoroutine(this.doorAnimationCoroutine);
            }
            
            this.doorAnimationCoroutine = StartCoroutine(this.DoorAnimation());
        }

        private IEnumerator DoorAnimation() {
            this.animator.SetBool("isOpened", true);
            yield return new WaitForSeconds(0.5f);
            this.animator.SetBool("isOpened", false);
            this.doorAnimationCoroutine = null;
        }
    }
}