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
        [SerializeField] private PropsConfig packageConfig;
        [SerializeField] private BuildSurfaceEnum surfaceToPose;
        [SerializeField] private bool toBuild;
        [SerializeField] private float rangeToInteract;
        [SerializeField] private Action[] actions;
        [SerializeField] private Action[] unbuiltActions;

        public Action[] GetUnbuiltActions() {
            return this.unbuiltActions;
        }
        
        public Action[] GetActions() {
            return this.actions;
        }
        
        public int GetId() {
            return this.id;
        }

        public float GetRangeToInteract() {
            return this.rangeToInteract;
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

        public PropsConfig GetPackageConfig() {
            return this.packageConfig;
        }

        public bool MustBeBuilt() {
            return this.toBuild;
        }
    }
}