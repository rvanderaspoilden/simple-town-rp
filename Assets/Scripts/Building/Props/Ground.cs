using UnityEngine;

namespace Sim.Building {
    public class Ground : Props {
        [Header("Settings")]
        [SerializeField] private Material materialToApply;

        private new Renderer renderer;

        private void Awake() {
            this.renderer = GetComponent<Renderer>();
        }

        private void Start() {
            this.ApplyMaterial();
        }

        public void SetMaterialToApply(Material material) {
            this.materialToApply = material;
            this.ApplyMaterial();
        }

        private void ApplyMaterial() {
            this.renderer.material = this.materialToApply;
        }
    }
}
