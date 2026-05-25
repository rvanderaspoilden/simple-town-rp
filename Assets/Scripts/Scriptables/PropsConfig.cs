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
        private Sprite sprite;

        [SerializeField]
        private PropsType propsType;

        [SerializeField]
        private int price;

        [SerializeField, Tooltip("Can this prop be sold? Drives BOTH its availability in the phone shop AND player-to-player listing. Tick on furniture; leave off for doors, lights, delivery boxes, packages, etc.")]
        private bool sellable;

        [SerializeField]
        private PropBehaviourBase prefab;

        [SerializeField]
        private PropsConfig packageConfig;

        [SerializeField]
        private BuildSurfaceEnum surfaceToPose;

        [SerializeField]
        private bool posableOnProps;

        [SerializeField]
        private bool hasPosableSurface;

        [SerializeField]
        private bool connectedToWall;

        [SerializeField]
        private bool toBuild;

        [SerializeField, Tooltip("Can the owner move this prop once built (generic MOVE action)? On by default; untick for fixtures like the delivery box, doors, lights.")]
        private bool movable = true;

        [SerializeField]
        private float rangeToInteract;

        [SerializeField]
        private bool rightClickOnly;

        [SerializeField]
        private Action[] actions;

        [SerializeField]
        private Action[] unbuiltActions;

        [SerializeField]
        private Texture2D cursor;

        [SerializeField]
        private PropsPreset[] presets;

        [SerializeField]
        private AudioClip buildSound;

        public PropsPreset[] Presets => presets;

        public Texture2D GetCursor() {
            return this.cursor;
        }

        public PropsType GetPropsType() {
            return this.propsType;
        }

        public AudioClip BuildSound => buildSound;

        public bool NeedToBeConnectedToWall() {
            return this.connectedToWall;
        }

        public Action[] GetUnbuiltActions() {
            return this.unbuiltActions;
        }

        public Action[] GetActions() {
            return this.actions;
        }

        public bool IsPosableOnProps() {
            return this.posableOnProps;
        }

        public bool IsSellable() {
            return this.sellable;
        }

        public int Price => price;

        public int GetId() {
            return this.id;
        }

        public Sprite Sprite => sprite;

        public float GetRangeToInteract() {
            return this.rangeToInteract;
        }

        public bool IsRightClickOnly() {
            return this.rightClickOnly;
        }

        public PropBehaviourBase GetPrefab() {
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

        public bool HasPosableSurface => hasPosableSurface;

        public bool MustBeBuilt() {
            return this.toBuild;
        }

        public bool IsMovable() {
            return this.movable;
        }
    }
}