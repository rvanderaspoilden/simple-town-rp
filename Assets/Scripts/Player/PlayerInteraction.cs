using System.Collections;
using System.Linq;
using Mirror;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using UnityEngine.Networking;

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
            if (!isLocalPlayer) return;

            BuildManager.OnCancel += OnBuildModificationCanceled;
            BuildManager.OnValidatePropCreation += OnValidatePropCreation;
            BuildManager.OnValidatePropEdit += OnValidatePropEdit;
            BuildManager.OnValidatePaintModification += OnValidatePaintModification;
            Props.OnMoveRequest += OnMoveRequest;
            PaintBucket.OnOpened += OpenBucket;
            DeliveryBox.UnPackage += OpenPackageFromDeliveryBox;
        }

        public override void OnStartLocalPlayer() {
            this.player.SetState(StateType.FREE);
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

            /*if (this.currentOpenedBucket.GetPaintConfig().IsWallCover()) {
                RoomManager.Instance.SetWallVisibility(VisibilityModeEnum.FORCE_SHOW);
            }*/ // TODO 

            BuildManager.Instance.Init(this.currentOpenedBucket);
        }

        private void OnBuildModificationCanceled() {
            this.currentOpenedBucket = null;
            this.player.SetState(StateType.FREE);
        }

        private void OnValidatePaintModification() {
            ApartmentController apartmentController = this.currentOpenedBucket.GetComponentInParent<ApartmentController>();
            
            if (this.currentOpenedBucket.GetPaintConfig().IsWallCover()) {
                apartmentController.ApplyWallSettings();
            } else if (this.currentOpenedBucket.GetPaintConfig().IsGroundCover()) {
                apartmentController.ApplyGroundSettings();
            }

            this.CmdDestroyProps(this.currentOpenedBucket.netId);

            this.player.SetState(StateType.FREE);
        }

        [Command(requiresAuthority = false)]
        public void CmdDestroyProps(uint netId) {
            if (!NetworkIdentity.spawned.ContainsKey(netId)) {
                Debug.LogError($"Server: Try to destroy {netId} but it not exist");
            }

            GameObject propsObject = NetworkIdentity.spawned[netId].gameObject;

            ApartmentController apartmentController = propsObject.GetComponentInParent<ApartmentController>();

            NetworkServer.Destroy(propsObject);
            
            StartCoroutine(apartmentController.Save());
        }

        private void OnValidatePropCreation(PropsConfig propsConfig, int presetId, Vector3 position, Quaternion rotation) {
            uint deliveryBoxNetId = PlayerController.Local.GetInteractedProps().netId;

            if (deliveryBoxNetId > 0) {
                this.CmdValidatePropCreation(deliveryBoxNetId, this.currentDelivery, propsConfig.GetId(), presetId, position, rotation);
            } else {
                Debug.LogError("Client: OnValidatePropCreation not found deliveryBoxNetId");
            }
        }

        [Command(requiresAuthority = false)]
        public void CmdValidatePropCreation(uint deliveryBoxNetId, Delivery delivery, int propsConfigId, int presetId, Vector3 position, Quaternion rotation,
            NetworkConnectionToClient sender = null) {
            if (!NetworkIdentity.spawned.ContainsKey(deliveryBoxNetId)) {
                Debug.LogError("Server: CmdValidatePropCreation not found deliveryBoxNetId");
                return;
            }

            DeliveryBox deliveryBox = NetworkIdentity.spawned[deliveryBoxNetId].GetComponent<DeliveryBox>();
            ApartmentController apartmentController = deliveryBox.GetComponentInParent<ApartmentController>();
            PropsConfig propsConfig = DatabaseManager.PropsDatabase.GetPropsById(propsConfigId);
            Props props = PropsManager.Instance.InstantiateProps(propsConfig, presetId, position, rotation);
            props.ParentId = apartmentController.netId;
            props.transform.SetParent(apartmentController.PropsContainer);
            props.InitBuilt(!propsConfig.MustBeBuilt());
            props.ApartmentController = apartmentController;

            if (delivery.Type.Equals(DeliveryType.COVER)) {
                PaintBucket coverProps = props as PaintBucket;

                if (coverProps) {
                    Debug.Log("Set cover properties");
                    coverProps.Init(delivery.PaintConfigId, delivery.Color);
                }
            }

            StartCoroutine(this.DeleteDeliveryCoroutine(apartmentController, deliveryBox, delivery, props, sender));
        }

        [Server]
        private IEnumerator DeleteDeliveryCoroutine(ApartmentController apartmentController, DeliveryBox deliveryBox, Delivery delivery, Props props,
            NetworkConnectionToClient sender) {
            UnityWebRequest request = ApiManager.Instance.DeleteDeliveryRequest(delivery);

            yield return request.SendWebRequest();

            if (request.responseCode == 200) {
                NetworkServer.Spawn(props.gameObject);

                Debug.Log("Server: Props spawned, now we need to save home");
                StartCoroutine(apartmentController.Save());

                Debug.Log("Server: Retrieve deliveries");
                yield return StartCoroutine(deliveryBox.RetrieveDeliveries());

                Debug.Log("Server: Refresh player view");
                this.TargetPropsCreated(sender);

                deliveryBox.RefreshPlayerUI(sender);
            } else {
                Destroy(props.gameObject);
                Debug.LogError($"Cannot delete delivery ID {delivery._id} from server");
            }
        }

        [TargetRpc]
        public void TargetPropsCreated(NetworkConnection conn) {
            this.player.SetState(StateType.FREE);
        }

        [Client]
        private void OnValidatePropEdit(Props props) {
            this.CmdPropEdit(props.netId, props.transform.localPosition, props.transform.localRotation);
        }

        [Command(requiresAuthority = false)]
        public void CmdPropEdit(uint propNetId, Vector3 localPosition, Quaternion localRotation, NetworkConnectionToClient sender = null) {
            if (!NetworkIdentity.spawned.ContainsKey(propNetId)) {
                Debug.LogError($"Server: propNetId {propNetId} not found");
                this.TargetPropEdit(sender, false);
            }

            Props props = NetworkIdentity.spawned[propNetId].GetComponent<Props>();
            props.transform.localPosition = localPosition;
            props.transform.localRotation = localRotation;

            StartCoroutine(props.GetComponentInParent<ApartmentController>().Save());

            this.TargetPropEdit(sender, true);
        }

        [TargetRpc]
        public void TargetPropEdit(NetworkConnection conn, bool result) {
            if (result) {
                BuildManager.Instance.EditionIsValidated();
            } else {
                Debug.LogError("Client: PropEdit failed so reset local props transform");
                BuildManager.Instance.Reset();
            }

            this.player.SetState(StateType.FREE);
        }
    }
}