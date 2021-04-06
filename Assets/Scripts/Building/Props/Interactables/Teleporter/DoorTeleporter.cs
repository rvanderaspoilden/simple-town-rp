using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using Sim.Constants;
using Sim.Entities;
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
            if (SceneManager.GetActiveScene().name.Equals(SceneConstants.HOME)) {
                this.numberText.enabled = false;
            }
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            
            StopAllCoroutines();
        }

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.TELEPORT)) {
                // Play open animation for all
                photonView.RPC("RPC_Animation", RpcTarget.All);

                if (this.destination.Equals(RoomTypeEnum.HOME)) {
                    //TODO: refacto this
                    Address address = new Address {Street = "SALMON HOTEL", DoorNumber = this.number, HomeType = HomeTypeEnum.APARTMENT};

                    //NetworkManager.Instance.GoToRoom(RoomTypeEnum.HOME, address);
                } else {
                    base.Execute(action);
                }
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