using System.Linq;
using Photon.Pun;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Interactables;
using Sim.Scriptables;
using Sim.UI;
using UnityEngine;

namespace Sim {
    [RequireComponent(typeof(Character))]
    public class PlayerInteraction : MonoBehaviourPun {
        [Header("DEBUG")]
        private Delivery currentDelivery;

        private PaintBucket currentOpenedBucket;

        private Character character;

        private void Awake() {
            this.character = GetComponent<Character>();
        }

        private void Start() {
            if (!photonView.IsMine) Destroy(this);

            BuildManager.OnCancel += OnBuildModificationCanceled;
            BuildManager.OnValidatePropCreation += OnValidatePropCreation;
            BuildManager.OnValidatePropEdit += OnValidatePropEdit;
            BuildManager.OnValidatePaintModification += OnValidatePaintModification;
            Props.OnMoveRequest += OnMoveRequest;
            PaintBucket.OnOpened += OpenBucket;
            DeliveryBox.UnPackage += OpenPackageFromDeliveryBox;

            this.character.SetState(StateType.FREE);
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F) && this.character.GetState() == StateType.FREE && PhotonNetwork.IsMasterClient && ApartmentManager.Instance &&
                ApartmentManager.Instance.IsTenant(NetworkManager.Instance.CharacterData)) {
                HUDManager.Instance.DisplayAdminPanel(true);
            }
        }

        private void OnDestroy() {
            BuildManager.OnCancel -= OnBuildModificationCanceled;
            BuildManager.OnValidatePropCreation -= OnValidatePropCreation;
            BuildManager.OnValidatePropEdit -= OnValidatePropEdit;
            BuildManager.OnValidatePaintModification -= OnValidatePaintModification;
            Props.OnMoveRequest -= OnMoveRequest;
            PaintBucket.OnOpened -= OpenBucket;
            DeliveryBox.UnPackage -= OpenPackageFromDeliveryBox;
        }

        private void OnMoveRequest(Props props) {
            this.character.SetState(StateType.MOVING_PROPS);

            BuildManager.Instance.Edit(props);
        }


        private void OpenPackageFromDeliveryBox(Delivery delivery) {
            this.currentDelivery = delivery;

            this.character.SetState(StateType.UNPACKAGING);

            BuildManager.Instance.Init(delivery);
        }

        private void OpenBucket(PaintBucket bucket) {
            this.currentOpenedBucket = bucket;

            this.character.SetState(StateType.PAINTING);

            if (this.currentOpenedBucket.GetPaintConfig().IsWallCover()) {
                RoomManager.Instance.SetWallVisibility(VisibilityModeEnum.FORCE_SHOW);
            }

            BuildManager.Instance.Init(this.currentOpenedBucket);
        }

        private void OnBuildModificationCanceled() {
            this.currentOpenedBucket = null;
            this.currentDelivery = null;
            this.character.SetState(StateType.FREE);
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

            this.character.SetState(StateType.FREE);
        }

        private void OnValidatePropCreation(PropsConfig propsConfig, int presetId, Vector3 position, Quaternion rotation) {
            Props props = PropsManager.Instance.InstantiateProps(propsConfig, presetId, position, rotation, true);

            // Manage unpackaging from delivery box
            if (this.character.GetState() == StateType.UNPACKAGING) {
                props.SetIsBuilt(!propsConfig.MustBeBuilt());

                if (this.currentDelivery.Type.Equals(DeliveryType.COVER)) {
                    PaintBucket coverProps = props as PaintBucket;

                    if (coverProps) {
                        Debug.Log("Set cover properties");
                        coverProps.SetPaintConfigId(this.currentDelivery.PaintConfigId, RpcTarget.All);
                        coverProps.SetColor(this.currentDelivery.Color, RpcTarget.All);
                    }
                }

                // TODO remove delivery
            }

            RoomManager.Instance.SaveRoom();

            this.character.SetState(StateType.FREE);
        }

        private void OnValidatePropEdit(Props props) {
            props.UpdateTransform();

            RoomManager.Instance.SaveRoom();

            this.character.SetState(StateType.FREE);
        }
    }
}