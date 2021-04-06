using System.Linq;
using Mirror;
using Photon.Pun;
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

        private PaintBucket currentOpenedBucket;

        private PlayerController player;

        private void Awake() {
            this.player = GetComponent<PlayerController>();
        }

        public override void OnStartClient() {
            if (!isLocalPlayer) Destroy(this);

            BuildManager.OnCancel += OnBuildModificationCanceled;
            BuildManager.OnValidatePropCreation += OnValidatePropCreation;
            BuildManager.OnValidatePropEdit += OnValidatePropEdit;
            BuildManager.OnValidatePaintModification += OnValidatePaintModification;
            Props.OnMoveRequest += OnMoveRequest;
            PaintBucket.OnOpened += OpenBucket;
            DeliveryBox.UnPackage += OpenPackageFromDeliveryBox;
            ApiManager.OnDeliveryDeleted += OnDeliveryDeleted;
        }

        public override void OnStartLocalPlayer() {
            this.player.SetState(StateType.FREE);
        }

        private void Update() {
            /*if (Input.GetKeyDown(KeyCode.F) && this.character.GetState() == StateType.FREE && PhotonNetwork.IsMasterClient && ApartmentManager.Instance &&
                ApartmentManager.Instance.IsTenant(NetworkManager.Instance.CharacterData)) {
                HUDManager.Instance.DisplayAdminPanel(true);
            }*/
        }

        private void OnDestroy() {
            if (isLocalPlayer) {
                BuildManager.OnCancel -= OnBuildModificationCanceled;
                BuildManager.OnValidatePropCreation -= OnValidatePropCreation;
                BuildManager.OnValidatePropEdit -= OnValidatePropEdit;
                BuildManager.OnValidatePaintModification -= OnValidatePaintModification;
                Props.OnMoveRequest -= OnMoveRequest;
                PaintBucket.OnOpened -= OpenBucket;
                DeliveryBox.UnPackage -= OpenPackageFromDeliveryBox;
                ApiManager.OnDeliveryDeleted -= OnDeliveryDeleted;
            }
        }

        private void OnMoveRequest(Props props) {
            this.player.SetState(StateType.MOVING_PROPS);

            BuildManager.Instance.Edit(props);
        }


        private void OpenPackageFromDeliveryBox(Delivery delivery) {
            this.currentDelivery = delivery;

            this.player.SetState(StateType.UNPACKAGING);

            BuildManager.Instance.Init(delivery);
        }

        private void OpenBucket(PaintBucket bucket) {
            this.currentOpenedBucket = bucket;

            this.player.SetState(StateType.PAINTING);

            if (this.currentOpenedBucket.GetPaintConfig().IsWallCover()) {
                RoomManager.Instance.SetWallVisibility(VisibilityModeEnum.FORCE_SHOW);
            }

            BuildManager.Instance.Init(this.currentOpenedBucket);
        }

        private void OnBuildModificationCanceled() {
            this.currentOpenedBucket = null;
            this.currentDelivery = null;
            this.player.SetState(StateType.FREE);
        }

        private void OnValidatePaintModification() {
            if (this.currentOpenedBucket.GetPaintConfig().IsWallCover()) {
                RoomManager.Instance.SetWallVisibility(VisibilityModeEnum.AUTO);

                FindObjectsOfType<Wall>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.ApplyModification());
            } else if (this.currentOpenedBucket.GetPaintConfig().IsGroundCover()) {
                FindObjectsOfType<Ground>().ToList().Where(x => x.IsPreview()).ToList().ForEach(x => x.ApplyModification());
            }

            PropsManager.Instance.DestroyProps(this.currentOpenedBucket, true);

            RoomManager.Instance.SaveRoom();

            this.player.SetState(StateType.FREE);
        }

        private void OnValidatePropCreation(PropsConfig propsConfig, int presetId, Vector3 position, Quaternion rotation) {
            Props props = PropsManager.Instance.InstantiateProps(propsConfig, presetId, position, rotation, true);

            props.SetIsBuilt(!propsConfig.MustBeBuilt());

            if (this.currentDelivery.Type.Equals(DeliveryType.COVER)) {
                PaintBucket coverProps = props as PaintBucket;

                if (coverProps) {
                    Debug.Log("Set cover properties");
                    coverProps.SetPaintConfigId(this.currentDelivery.PaintConfigId, RpcTarget.All);
                    coverProps.SetColor(this.currentDelivery.Color, RpcTarget.All);
                }
            }

            ApiManager.Instance.DeleteDelivery(this.currentDelivery);
        }

        private void OnDeliveryDeleted(bool isDeleted) {
            if (isDeleted) {
                RoomManager.Instance.SaveRoom();

                this.player.SetState(StateType.FREE);
            } else {
                // TODO DISCONNECT
            }
        }

        private void OnValidatePropEdit(Props props) {
            props.UpdateTransform();

            RoomManager.Instance.SaveRoom();

            this.player.SetState(StateType.FREE);
        }
    }
}