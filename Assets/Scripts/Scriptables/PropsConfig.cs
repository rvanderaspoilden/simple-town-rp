using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "Props", menuName = "Configurations/Props")]
    public class PropsConfig : ScriptableObject {
        [SerializeField]
        private int id;

        [SerializeField]
        private string displayName;

        [SerializeField]
        private PropsType propsType;

        [SerializeField]
        private bool buyable;

        [SerializeField]
        private Props prefab;

        [SerializeField]
        private PropsConfig packageConfig;

        [SerializeField]
        private BuildSurfaceEnum surfaceToPose;

        [SerializeField]
        private bool toBuild;

        [SerializeField]
        private float rangeToInteract;

        [SerializeField]
        private Action[] actions;

        [SerializeField]
        private Action[] unbuiltActions;

        [SerializeField]
        private Texture2D cursor;

        public Texture2D GetCursor() {
            return this.cursor;
        }

        public PropsType GetPropsType() {
            return this.propsType;
        }

        public Action[] GetUnbuiltActions() {
            return this.unbuiltActions;
        }

        public Action[] GetActions() {
            return this.actions;
        }

        public bool IsBuyable() {
            return this.buyable;
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