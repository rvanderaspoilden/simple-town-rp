using Enums;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "New Food Container", menuName = "Configurations/Building/Food Container")]
    public class B_FoodContainerConfig : BuildingConfig {
        [SerializeField]
        private B_FoodContainerType containerType;

        public B_FoodContainerType ContainerType => containerType;
    }
}