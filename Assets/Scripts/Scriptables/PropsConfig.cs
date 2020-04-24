using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "Props", menuName = "Configurations/Props")]
    public class PropsConfig : ScriptableObject {
        [SerializeField] private int id;
        [SerializeField] private string displayName;
        [SerializeField] private Props prefab;
        [SerializeField] private Package packagePrefab;
        [SerializeField] private BuildSurfaceEnum surfaceToPose;
        [SerializeField] private bool toBuild;
        
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

        public Package GetPackage() {
            return this.packagePrefab;
        }

        public bool MustBeBuilt() {
            return this.toBuild;
        }
    }
}