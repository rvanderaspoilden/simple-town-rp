using Mirror;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

namespace Sim {
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteraction : NetworkBehaviour {
        [Header("DEBUG")]
        private Delivery currentDelivery;

        private PaintBucketBehaviour currentOpenedBucket;
        private DeliveryBoxBehaviour currentDeliveryBox;

        private PlayerController player;

        private void Awake() {
            this.player = GetComponent<PlayerController>();
        }

        public override void OnStartClient() {
            if (!isLocalPlayer) return;

            BuildManager.OnCancel                    += OnBuildModificationCanceled;
            BuildManager.OnValidatePropCreation      += OnValidatePropCreation;
            BuildManager.OnValidatePropEdit          += OnValidatePropEdit;
            BuildManager.OnValidatePaintModification += OnValidatePaintModification;
            PropBehaviourBase.OnMoveRequest          += OnMoveRequest;
            PropBehaviourBase.OnSellRequest          += OnSellRequest;
            PropBehaviourBase.OnListForSaleRequest   += OnListForSaleRequest;
            PropBehaviourBase.OnUnlistRequest        += OnUnlistRequest;
            PaintBucketBehaviour.OnOpened            += OpenBucket;
            DeliveryBoxBehaviour.OnOpened            += OnDeliveryBoxOpened;
            DeliveryBoxBehaviour.UnPackage           += OpenPackageFromDeliveryBox;
            DispenserBehaviour.OnOpened              += OnDispenserOpened;
            PackageBehaviour.OnOpened                += OnPackageOpened;
            ClientPropManager.OnBuildAckReceived     += OnBuildAck;
        }

        public override void OnStartLocalPlayer() {
            this.player.SetState(StateType.FREE);
        }

        private void OnDestroy() {
            if (isLocalPlayer) {
                BuildManager.OnCancel                    -= OnBuildModificationCanceled;
                BuildManager.OnValidatePropCreation      -= OnValidatePropCreation;
                BuildManager.OnValidatePropEdit          -= OnValidatePropEdit;
                BuildManager.OnValidatePaintModification -= OnValidatePaintModification;
                PropBehaviourBase.OnMoveRequest          -= OnMoveRequest;
                PropBehaviourBase.OnSellRequest          -= OnSellRequest;
                PropBehaviourBase.OnListForSaleRequest   -= OnListForSaleRequest;
                PropBehaviourBase.OnUnlistRequest        -= OnUnlistRequest;
                PaintBucketBehaviour.OnOpened            -= OpenBucket;
                DeliveryBoxBehaviour.OnOpened            -= OnDeliveryBoxOpened;
                DeliveryBoxBehaviour.UnPackage           -= OpenPackageFromDeliveryBox;
                DispenserBehaviour.OnOpened              -= OnDispenserOpened;
                PackageBehaviour.OnOpened                -= OnPackageOpened;
                ClientPropManager.OnBuildAckReceived     -= OnBuildAck;
            }
        }

        private void OnMoveRequest(PropBehaviourBase behaviour) {
            this.player.SetState(StateType.MOVING_PROPS);
            BuildManager.Instance.Edit(behaviour);
        }

        private void OnSellRequest(PropBehaviourBase behaviour) {
            // New system: prop has propId/roomId on its PropIdentity
            PropIdentity id = behaviour.GetComponent<PropIdentity>();
            if (id == null || id.PropId <= 0) return;
            NetworkClient.Send(new C2S_RemoveProp { RoomId = id.RoomId, PropId = id.PropId });
        }

        /// <summary>
        /// Owner listed a prop for sale. A gift (isGift) lists at price 0 immediately;
        /// a regular sale needs a price, so the price-input UI subscribes to the same
        /// PropBehaviourBase.OnListForSaleRequest event and calls
        /// ClientPropManager.RequestSetForSale with the entered amount.
        /// </summary>
        private void OnListForSaleRequest(PropBehaviourBase behaviour, bool isGift) {
            if (!isGift) return; // priced listing handled by the price-input UI
            PropIdentity id = behaviour.GetComponent<PropIdentity>();
            if (id == null || id.PropId <= 0) return;
            ClientPropManager.Instance?.RequestSetForSale(id.PropId, 0);
        }

        private void OnUnlistRequest(PropBehaviourBase behaviour) {
            PropIdentity id = behaviour.GetComponent<PropIdentity>();
            if (id == null || id.PropId <= 0) return;
            ClientPropManager.Instance?.RequestUnlist(id.PropId);
        }

        private void OnDispenserOpened(DispenserBehaviour dispenser) {
            // Open the dispenser UI for the new prop system
            DefaultViewUI.Instance.ShowPropsContentUI(dispenser);
        }

        private void OnPackageOpened(PackageBehaviour package) {
            // Enter UNPACKAGING state and init build mode with the PropsConfig inside the package
            this.player.SetState(StateType.UNPACKAGING);
            BuildManager.Instance.Init(package.GetPropsConfigInside());
        }

        private void OnDeliveryBoxOpened(DeliveryBoxBehaviour box, Delivery[] _) {
            this.currentDeliveryBox = box;
            // Open the delivery box UI for the new prop system
            DefaultViewUI.Instance.ShowPropsContentUI(box);
        }

        private void OpenPackageFromDeliveryBox(Delivery delivery) {
            this.currentDelivery = delivery;
            this.player.SetState(StateType.UNPACKAGING);
            BuildManager.Instance.Init(delivery);
        }

        private void OpenBucket(PaintBucketBehaviour bucket) {
            this.currentOpenedBucket = bucket;
            this.player.SetState(StateType.PAINTING);
            BuildManager.Instance.Init(this.currentOpenedBucket);
        }

        private void OnBuildModificationCanceled() {
            this.currentOpenedBucket = null;
            this.player.SetState(StateType.FREE);
        }

        private void OnValidatePaintModification() {
            // In client-only mode the bucket is not reparented under the apartment, so
            // GetComponentInParent<ApartmentController> would return null. Use the apartment
            // resolved by BuildManager when paint mode was entered.
            ApartmentController apartmentController = BuildManager.Instance.CurrentApartment;
            if (apartmentController == null) {
                Debug.LogError("[PlayerInteraction] OnValidatePaintModification: no current apartment");
                return;
            }

            if (this.currentOpenedBucket.GetPaintConfig().IsWallCover()) {
                apartmentController.ApplyWallSettings();
            } else if (this.currentOpenedBucket.GetPaintConfig().IsGroundCover()) {
                apartmentController.ApplyGroundSettings();
            }

            // Destroy the bucket via the new prop system
            PropIdentity id = this.currentOpenedBucket.GetComponent<PropIdentity>();
            if (id != null) {
                NetworkClient.Send(new C2S_RemoveProp { RoomId = id.RoomId, PropId = id.PropId });
            }

            this.player.SetState(StateType.FREE);
        }

        private void OnValidatePropCreation(PropsConfig propsConfig, int presetId, Vector3 position, Quaternion rotation) {
            if (this.currentDeliveryBox == null) {
                Debug.LogError("[PlayerInteraction] OnValidatePropCreation without an opened delivery box");
                return;
            }

            PropIdentity boxId = this.currentDeliveryBox.GetComponent<PropIdentity>();
            if (boxId == null) {
                Debug.LogError("[PlayerInteraction] DeliveryBox has no PropIdentity");
                return;
            }

            float r = 1f, g = 1f, b = 1f;
            int paintConfigId = -1;
            if (this.currentDelivery.Type == DeliveryType.COVER) {
                paintConfigId = this.currentDelivery.PaintConfigId;
                if (this.currentDelivery.Color != null && this.currentDelivery.Color.Length >= 3) {
                    r = this.currentDelivery.Color[0];
                    g = this.currentDelivery.Color[1];
                    b = this.currentDelivery.Color[2];
                }
            }

            NetworkClient.Send(new C2S_BuildProp {
                RoomId            = boxId.RoomId,
                DeliveryBoxPropId = boxId.PropId,
                DeliveryId        = this.currentDelivery._id,
                PropConfigId      = propsConfig.GetId(),
                PresetId          = presetId,
                Position          = position,
                Rotation          = rotation,
                PaintConfigId     = paintConfigId,
                ColorR            = r,
                ColorG            = g,
                ColorB            = b,
            });
        }

        private void OnValidatePropEdit(PropBehaviourBase behaviour) {
            PropIdentity id = behaviour.GetComponent<PropIdentity>();
            if (id == null || id.PropId <= 0) {
                Debug.LogError("[PlayerInteraction] OnValidatePropEdit: prop has no PropIdentity");
                return;
            }

            NetworkClient.Send(new C2S_EditProp {
                RoomId   = id.RoomId,
                PropId   = id.PropId,
                Position = behaviour.transform.position,
                Rotation = behaviour.transform.rotation,
            });

            BuildManager.Instance.EditionIsValidated();
            this.player.SetState(StateType.FREE);
        }

        private void OnBuildAck(bool success) {
            if (success) {
                this.player.SetState(StateType.FREE);
            } else {
                Debug.LogError("[PlayerInteraction] Build failed server-side");
                BuildManager.Instance.Reset();
                this.player.SetState(StateType.FREE);
            }
        }
    }
}
