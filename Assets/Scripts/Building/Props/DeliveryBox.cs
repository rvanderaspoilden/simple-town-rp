using System.Collections.Generic;
using System.Linq;
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

        [Header("Debug")]
        [SerializeField]
        private Delivery[] deliveries;

        public delegate void UnPackageEvent(Delivery delivery);

        public static event UnPackageEvent UnPackage;

        protected override void Start() {
            base.Start();

            if (ApartmentManager.Instance.IsTenant(NetworkManager.Instance.CharacterData)) {
                Debug.Log("Retrieve deliveries....");
                ApiManager.OnDeliveriesRetrieved += OnDeliveriesRetrieved;
                PropsContentUI.OnSelect += OnSelectDelivery;

                ApiManager.Instance.RetrieveDeliveries(NetworkManager.Instance.CharacterData.Id);
            }
        }

        protected override void OnDestroy() {
            base.OnDestroy();

            if (ApartmentManager.Instance.IsTenant(NetworkManager.Instance.CharacterData)) {
                ApiManager.OnDeliveriesRetrieved -= OnDeliveriesRetrieved;
                PropsContentUI.OnSelect -= OnSelectDelivery;
            }
        }

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.OPEN)) {
                RoomManager.LocalCharacter.Interact(this);
                DefaultViewUI.Instance.SetupPropsContentUI(deliveries.Select(x => x.DisplayName()).ToArray());
            }
        }

        public override void StopInteraction() {
            DefaultViewUI.Instance.HidePropsContentUI();
        }

        private void OnSelectDelivery(int idx) {
            UnPackage?.Invoke(this.deliveries[idx]);
        }

        public Delivery[] Deliveries {
            get => deliveries;
            set => deliveries = value;
        }

        private void OnDeliveriesRetrieved(List<Delivery> value) {
            this.deliveries = value.ToArray();
            Debug.Log($"I retrieved {this.deliveries.Length} deliveries");

            this.UpdateGraphics();
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