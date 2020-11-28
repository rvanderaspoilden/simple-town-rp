using System;
using System.Linq;
using Photon.Pun;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using Sim.Scriptables;
using Sim.UI;
using UnityEngine;

namespace Sim {
    [RequireComponent(typeof(Player))]
    public class PlayerInteraction : MonoBehaviourPun {
        [Header("DEBUG")]
        private Package currentOpenedPackage;

        private PropsConfig propsToPackage;

        private PaintBucket currentOpenedBucket;

        private PaintConfig paintToPackage;

        private Player player;

        private void Awake() {
            this.player = GetComponent<Player>();
        }

        private void Start() {
            if (!photonView.IsMine) Destroy(this);

            AliDiscountCatalogUI.OnPropsClicked += OnSelectPropsFromAdminPanel;
            AliDiscountCatalogUI.OnPaintClicked += OnSelectPaintFromAdminPanel;
            BuildManager.OnCancel += OnBuildModificationCanceled;
            BuildManager.OnValidatePropModification += OnValidatePropModification;
            BuildManager.OnValidatePaintModification += OnValidatePaintModification;
            Package.OnOpened += OpenPackage;
            PaintBucket.OnOpened += OpenBucket;
            
            this.player.SetState(StateType.FREE);
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F) && this.player.GetState() == StateType.FREE && PhotonNetwork.IsMasterClient && AppartmentManager.instance &&
                AppartmentManager.instance.IsOwner(NetworkManager.Instance.Personnage)) {
                HUDManager.Instance.DisplayAdminPanel(true);
            }
        }

        private void OnDestroy() {
            AliDiscountCatalogUI.OnPropsClicked -= OnSelectPropsFromAdminPanel;
            AliDiscountCatalogUI.OnPaintClicked -= OnSelectPaintFromAdminPanel;
            BuildManager.OnCancel -= OnBuildModificationCanceled;
            BuildManager.OnValidatePropModification -= OnValidatePropModification;
            BuildManager.OnValidatePaintModification -= OnValidatePaintModification;
            Package.OnOpened -= OpenPackage;
            PaintBucket.OnOpened -= OpenBucket;
        }

        /**
         * Called when props was chosen from admin panel
         */
        private void OnSelectPropsFromAdminPanel(PropsConfig propsConfig) {
            this.propsToPackage = propsConfig;

            this.player.SetState(StateType.PACKAGING);

            BuildManager.Instance.Init(this.propsToPackage.GetPackageConfig());
        }

        /**
         * Called when paint was chosen from admin panel
         */
        private void OnSelectPaintFromAdminPanel(PaintConfig paintConfig) {
            this.paintToPackage = paintConfig;

            this.player.SetState(StateType.PACKAGING);

            BuildManager.Instance.Init(this.paintToPackage.GetBucketPropsConfig());
        }

        private void OpenPackage(Package package) {
            this.currentOpenedPackage = package;

            this.player.SetState(StateType.UNPACKAGING);
            
            BuildManager.Instance.Init(package.GetPropsConfigInside());
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
            this.currentOpenedPackage = null;
            this.currentOpenedBucket = null;
            this.propsToPackage = null;
            this.paintToPackage = null;
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

        private void OnValidatePropModification(PropsConfig propsConfig, Vector3 position, Quaternion rotation) {
            Props props = PropsManager.Instance.InstantiateProps(propsConfig, position, rotation, true);

            // Manage packaging for props
            if (this.player.GetState() == StateType.PACKAGING && this.propsToPackage) {
                props.SetIsBuilt(true);
                props.GetComponent<Package>().SetPropsInside(this.propsToPackage.GetId(), RpcTarget.All);
                this.propsToPackage = null;
            }

            // Manage packaging for paint
            if (this.player.GetState() == StateType.PACKAGING && this.paintToPackage) {
                props.SetIsBuilt(true);
                props.GetComponent<PaintBucket>().SetPaintConfigId(this.paintToPackage.GetId(), RpcTarget.All);
                this.paintToPackage = null;
            }

            // Manage unpackaging
            if (this.player.GetState() == StateType.UNPACKAGING && this.currentOpenedPackage) {
                props.SetIsBuilt(!propsConfig.MustBeBuilt());
                PropsManager.Instance.DestroyProps(this.currentOpenedPackage, true);
                this.currentOpenedPackage = null;
            }

            RoomManager.Instance.SaveRoom();

            // this.SwitchToFreeMode(); // TODO Camera manager must subscribe to this event to come back to free camera
            this.player.SetState(StateType.FREE);
        }
    }
}