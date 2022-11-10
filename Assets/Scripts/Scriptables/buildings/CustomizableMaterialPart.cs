using System;
using UnityEngine;

namespace Sim.Scriptables {
    [Serializable]
    public struct CustomizableMaterialPart {
        public int id;
        public string question;
        public Color[] availableColors;
    }
}