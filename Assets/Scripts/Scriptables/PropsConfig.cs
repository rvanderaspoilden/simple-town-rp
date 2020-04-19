using Sim.Building;
using Sim.Enums;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "Props", menuName = "Configurations")]
    public class PropsConfig : ScriptableObject {
        [SerializeField] private int id;
        [SerializeField] private string displayName;
        [SerializeField] private Props prefab;
        [SerializeField] private BuildSurfaceEnum surfaceToPose;
        [SerializeField] private AxisEnum rotationAxis;

        public int GetId() {
            return this.id;
        }

        public Props GetPrefab() {
            return this.prefab;
        }

        public string GetDisplayName() {
            return this.displayName;
        }

        public BuildSurfaceEnum GetSurfaceToPose() {
            return this.surfaceToPose;
        }

        public AxisEnum GetRotationAxis() {
            return this.rotationAxis;
        }
    }   
}
