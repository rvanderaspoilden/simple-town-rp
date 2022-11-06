using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "New Food Container", menuName = "Configurations/Building/Food Container")]
    public class B_FoodContainerConfig : BuildingConfig {
        [SerializeField]
        private Color32[] mainColors;

        [SerializeField]
        private Color32[] barColors;

        [SerializeField]
        private Color32[] storeColors;

        public Color32[] MainColors => mainColors;

        public Color32[] BarColors => barColors;

        public Color32[] StoreColors => storeColors;
    }
}