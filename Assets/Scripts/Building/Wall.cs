using UnityEngine;

namespace Sim.Building {
    public class Wall : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private GameObject foundationObj;

        [SerializeField] private Renderer[] renderersToHide;

        [Header("Only for debug")]
        [SerializeField] private bool showFoundation;

        public void ShowFoundation(bool state) {
            if (this.showFoundation == state) { // Prevent useless treatments
                return;
            }

            this.showFoundation = state;
            this.UpdateGraphics();
        }

        private void UpdateGraphics() {
            foreach (Renderer renderer in this.renderersToHide) {
                renderer.enabled = !this.showFoundation;
            }

            this.foundationObj.SetActive(this.showFoundation);
        }
    }
}