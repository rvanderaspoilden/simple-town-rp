using UnityEngine;

namespace Sim.Scriptables {
    public class BuildingConfig : ScriptableObject {
        [SerializeField]
        private string label;

        [SerializeField]
        private float cost;

        [SerializeField]
        private GameObject prefab;

        public string Label => label;

        public float Cost => cost;

        public GameObject Prefab => prefab;
    }
}