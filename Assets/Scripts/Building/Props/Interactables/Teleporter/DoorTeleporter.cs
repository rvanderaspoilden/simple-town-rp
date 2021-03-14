﻿using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using Sim.Constants;
using Sim.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sim.Interactables {
    public class DoorTeleporter : Teleporter {
        [Header("Door Settings")]
        [SerializeField]
        private DoorDirectionEnum doorDirection = DoorDirectionEnum.FORWARD;

        [SerializeField]
        private int number;

        private Animator animator;
        private Coroutine doorAnimationCoroutine;
        private TextMeshPro numberText;

        protected override void Awake() {
            base.Awake();

            this.numberText = GetComponentInChildren<TextMeshPro>();
            this.animator = GetComponent<Animator>();
            this.animator.SetFloat("direction", doorDirection == DoorDirectionEnum.BACKWARD ? -1 : 1);

            // Hide door number in apartment
            if (SceneManager.GetActiveScene().name.Equals(PlaceName.HOME)) {
                this.numberText.enabled = false;
            }
        }

        private void OnDestroy() {
            StopAllCoroutines();
        }

        protected override void Use() {
            // Play open animation for all
            photonView.RPC("RPC_Animation", RpcTarget.All);

            if (this.destination == PlacesEnum.HOME) {
                NetworkManager.Instance.GoToHome(this.number);
            } else {
                base.Use();
            }
        }

        public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);

            this.SetDoorDirection(this.doorDirection, playerTarget);
            this.SetDoorNumber(this.number, playerTarget);
        }

        public void SetDoorNumber(int value, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetDoorNumber", rpcTarget, value);
        }

        public void SetDoorNumber(int value, Player playerTarget) {
            photonView.RPC("RPC_SetDoorNumber", playerTarget, value);
        }

        [PunRPC]
        public void RPC_SetDoorNumber(int value) {
            this.number = value;
            this.numberText.text = number.ToString();
        }

        public int GetDoorNumber() {
            return this.number;
        }

        public DoorDirectionEnum GetDoorDirection() {
            return this.doorDirection;
        }

        public void SetDoorDirection(DoorDirectionEnum doorDirectionEnum, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetDoorDirection", rpcTarget, doorDirectionEnum);
        }

        public void SetDoorDirection(DoorDirectionEnum doorDirectionEnum, Player playerTarget) {
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