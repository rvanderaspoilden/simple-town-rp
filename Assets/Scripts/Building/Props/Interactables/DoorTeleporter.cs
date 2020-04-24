using System;
using System.Collections;
using Photon.Pun;
using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public class DoorTeleporter : Teleporter {
        [Header("Door Settings")]
        [SerializeField] private DoorDirectionEnum doorDirection = DoorDirectionEnum.FORWARD;

        [SerializeField] private Action useAction;
        [SerializeField] private Action lockAction;

        private Animator animator;
        private Coroutine doorAnimationCoroutine;

        private void Awake() {
            this.animator = GetComponent<Animator>();
            this.animator.SetFloat("direction", (float)doorDirection);
        }

        private void OnDestroy() {
            StopAllCoroutines();
        }

        protected override void SetupActions() {
            this.actions = new Action[2] {
                this.useAction,
                this.lockAction
            };
        }

        public override void Use() {
            // Play open animation for all
            photonView.RPC("RPC_Animation", RpcTarget.All);
            
            base.Use();
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