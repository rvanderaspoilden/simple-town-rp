using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using Sim.Entities;
using Sim.Enums;
using Sim.Interactables;
using Sim.UI;
using UnityEngine;

namespace Sim.Building {
    public class DeliveryBox : Props {
        [Header("Settings")]
        [SerializeField]
        private Transform clapTransform;

        [SerializeField]
        private Quaternion openedClapRotation;

        [SerializeField]
        private GameObject package;

        [SerializeField]
        private AudioClip alertSound;

        [Header("Debug")]
        private Delivery[] deliveries;

        private AudioSource _audioSource;

        public delegate void UnPackageEvent(Delivery delivery);

        public static event UnPackageEvent UnPackage;

        protected override void Awake() {
            base.Awake();

            this._audioSource = GetComponent<AudioSource>();
            this.deliveries = new Delivery[0];
        }

        protected override void Start() {
            base.Start();

            /*if (ApartmentManager.Instance.IsTenant(NetworkManager.Instance.CharacterData)) {
                ApiManager.OnDeliveriesRetrieved += OnDeliveriesRetrieved;
                ApiManager.OnDeliveryDeleted += OnDeliveryDeleted;
                PropsContentUI.OnSelect += OnSelectDelivery;

                ApiManager.Instance.RetrieveDeliveries(NetworkManager.Instance.CharacterData.Id);
            }*/
        }

        protected override void OnDestroy() {
            base.OnDestroy();

            /*if (ApartmentManager.Instance.IsTenant(NetworkManager.Instance.CharacterData)) {
                ApiManager.OnDeliveriesRetrieved -= OnDeliveriesRetrieved;
                ApiManager.OnDeliveryDeleted -= OnDeliveryDeleted;
                PropsContentUI.OnSelect -= OnSelectDelivery;
            }*/
        }

        /*public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);

            /*if (ApartmentManager.Instance.IsTenant(NetworkManager.Instance.CharacterData)) {
                this.RefreshDeliveriesQuantity(playerTarget);
            }#1#
        }

        private void RefreshDeliveriesQuantity(Player playerTarget = null) {
            if (playerTarget != null) {
                photonView.RPC("RPC_RefreshDeliveriesQuantity", playerTarget, this.deliveries.Length);
            } else {
                photonView.RPC("RPC_RefreshDeliveriesQuantity", RpcTarget.Others, this.deliveries.Length);
            }
        }*/

        [PunRPC]
        public void RPC_RefreshDeliveriesQuantity(int quantity) {
            this.deliveries = new Delivery[quantity];
            this.UpdateGraphics();
        }

        protected override void Execute(Action action) {
            /*if (action.Type.Equals(ActionTypeEnum.OPEN) && ApartmentManager.Instance.IsTenant(NetworkManager.Instance.CharacterData)) {
                RoomManager.LocalCharacter.Interact(this);
                DefaultViewUI.Instance.ShowPropsContentUI(deliveries.Select(x => x.DisplayName()).ToArray());
            }*/
        }

        public override void StopInteraction() {
            DefaultViewUI.Instance.HidePropsContentUI();
        }

        private void OnSelectDelivery(int idx) {
            if (this.deliveries == null || this.deliveries.Length == 0) {
                RoomManager.LocalPlayer.Idle();
            } else {
                UnPackage?.Invoke(this.deliveries[idx]);
            }
        }

        public Delivery[] Deliveries {
            get => deliveries;
            set => deliveries = value;
        }

        private void OnDeliveriesRetrieved(List<Delivery> value) {
            this.deliveries = value.ToArray();

            this.UpdateGraphics();

            //this.RefreshDeliveriesQuantity();

            DefaultViewUI.Instance.RefreshPropsContentUI(deliveries.Select(x => x.DisplayName()).ToArray());
        }

        private void OnDeliveryDeleted(bool isDeleted) {
            if (!isDeleted) return;

            //ApiManager.Instance.RetrieveDeliveries(NetworkManager.Instance.CharacterData.Id);
        }

        private void UpdateGraphics() {
            if (this.deliveries?.Length > 0) {
                this.clapTransform.rotation = this.openedClapRotation;
                this.package.SetActive(true);
            } else {
                this.clapTransform.rotation = Quaternion.Euler(0, 0, 0);
                this.package.SetActive(false);
            }
        }
    }
}