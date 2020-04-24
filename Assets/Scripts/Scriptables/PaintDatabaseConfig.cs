using System.Collections.Generic;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "Paint Database", menuName = "Configurations/Database/Paint")]
    public class PaintDatabaseConfig : ScriptableObject {
        [SerializeField] private List<PaintConfig> paints;

        public List<PaintConfig> GetPaints() {
            return this.paints;
        }

        public PaintConfig GetPaintById(int id) {
            return paints.Find(x => x.GetId() == id);
        }
    }
}