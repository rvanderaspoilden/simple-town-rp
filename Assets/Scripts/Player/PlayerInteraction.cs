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
            BuildManager.OnValidatePropUnpack        += OnValidatePropUnpack;
            BuildManager.OnValidatePropEdit          += OnValidatePropEdit;
            BuildManager.OnValidatePaintModification += OnValidatePaintModification;
            PropBehaviourBase.OnMoveRequest          += OnMoveRequest;
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
                BuildManager.OnValidatePropUnpack        -= OnValidatePropUnpack;
                BuildManager.OnValidatePropEdit          -= OnValidatePropEdit;
                BuildManager.OnValidatePaintModification -= OnValidatePaintModification;
                PropBehaviourBase.OnMoveRequest          -= OnMoveRequest;
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

        /// <summary>
        /// Déballage d'un meuble emballé : entre en mode build (comme la delivery box) pour
        /// re-poser le meuble dans l'appartement. À la validation, le serveur DÉPLACE le
        /// meuble existant (UUID conservé) — cf. <see cref="OnValidatePropUnpack"/>.
        /// Appelé depuis la grille du colis (ContainerPanelUI) sur clic droit d'une entrée meuble.
        /// </summary>
        public void StartPropUnpack(PropsConfig config, int presetId, int packageEntityId, int slotIndex) {
            if (config == null) return;
            this.player.SetState(StateType.UNPACKAGING);
            BuildManager.Instance.InitUnpack(config, presetId, packageEntityId, slotIndex);
        }

        private void OnValidatePropUnpack(int packageEntityId, int slotIndex, Vector3 position, Quaternion rotation) {
            NetworkClient.Send(new C2S_UnpackProp {
                PackageEntityId = packageEntityId,
                SlotIndex       = slotIndex,
                Position        = position,
                Rotation        = rotation,
            });
            this.player.SetState(StateType.FREE);
        }

        private void OpenBucket(PaintBucketBehaviour bucket) {
            // Décoration: vérifié AU CLIC, AVANT tout changement d'état. Si le nœud
            // n'est pas débloqué → toast d'erreur et on n'entre pas en mode peinture
            // (ni état PAINTING, ni build). Fail-open si le provider n'est pas hydraté.
            var pc = this.player != null ? this.player.GetComponent<Sim.Player.PlayerConstellation>() : null;
            var provider = pc != null ? pc.Provider : null;
            if (provider != null &&
                !provider.State.IsUnlocked(Sim.Constellation.ConstellationPerks.CreatifDecorationNode)) {
                WorldToastManager.ShowError("Débloque « Décoration » pour peindre");
                return;
            }

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

            // Placement instantané : crée le prop (en état "à construire" si toBuild).
            // La construction temporisée (barre de progression) se fait ENSUITE via
            // l'action BUILD sur le prop (PropBehaviourBase.DoAction).
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
