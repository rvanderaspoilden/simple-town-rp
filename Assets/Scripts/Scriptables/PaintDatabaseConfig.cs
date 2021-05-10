using System.Collections.Generic;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "Paint Database", menuName = "Configurations/Database/Paint")]
    public class PaintDatabaseConfig : ScriptableObject {
        [SerializeField] private List<CoverConfig> paints;

        public List<CoverConfig> GetPaints() {
            return this.paints;
        }

        public CoverConfig GetPaintById(int id) {
            return paints.Find(x => x.GetId() == id);
        }
    }
}