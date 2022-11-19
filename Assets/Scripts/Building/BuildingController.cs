using Mirror;
using Network.Messages;
using Sim.Scriptables;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Sim.Building {
    public class BuildingController : NetworkBehaviour {
        [SerializeField]
        protected BuildingConfig config;

        [SerializeField]
        private Renderer[] customizableRenderers;

        private BuildArea _attachedArea;

        [SerializeField]
        [ReadOnly]
        [SyncVar(hook = nameof(OnBuildingDataChanged))]
        protected CreateBuildingMessage _buildingData;

        private void Awake() {
            // Create copy of each materials
            this.DuplicateMaterials();
        }

        public BuildArea AttachedArea {
            get => _attachedArea;
            set => _attachedArea = value;
        }

        [Server]
        public void SetCustomizedMaterialParts(CustomizedMaterialPart[] value) {
            this._buildingData = new CreateBuildingMessage() { customizedMaterialParts = value };
        }

        private void DuplicateMaterials() {
            foreach (var _renderer in customizableRenderers) {
                Material newMaterial = new Material(_renderer.material);
                _renderer.material = newMaterial;
            }
        }

        public override void OnStopServer() {
            if (!this.AttachedArea) return;

            this.AttachedArea.ResetState();
        }

        protected virtual void OnBuildingDataChanged(CreateBuildingMessage _, CreateBuildingMessage value) {
            Debug.Log("[BuildingController][Hook] Building Data changed");
            this.Customize(value.customizedMaterialParts);
        }

        private void Customize(CustomizedMaterialPart[] customizedMaterialParts) {
            foreach (var materialPart in customizedMaterialParts) {
                this.customizableRenderers[materialPart.id].material.color = materialPart.color;
            }
        }
    }
}