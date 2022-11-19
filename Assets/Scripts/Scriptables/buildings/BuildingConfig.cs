using Sim.Building;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Sim.Scriptables {
    public class BuildingConfig : ScriptableObject {
        [SerializeField]
        private int id;

        [SerializeField]
        private string label;

        [SerializeField]
        private Sprite picture;

        [SerializeField]
        private float cost;

        [SerializeField]
        private BuildingController prefab;

        [SerializeField]
        private bool customizable;

        [ShowIf("customizable")]
        [SerializeField]
        private CustomizableMaterialPart[] customizableMaterialParts;

        public int ID => id;

        public string Label => label;

        public Sprite Picture => picture;

        public float Cost => cost;

        public BuildingController Prefab => prefab;

        public bool IsCustomizable => customizable;

        public CustomizableMaterialPart[] CustomizableMaterialParts => customizableMaterialParts;
    }
}