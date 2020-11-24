using Sim.Enums;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "Cursor", menuName = "Configurations/Cursor")]
    public class CursorConfig : ScriptableObject {
        [SerializeField]
        private Texture2D texture2D;

        [SerializeField]
        private CursorTypeEnum type;
    }
}
