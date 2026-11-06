using UnityEngine;

namespace Sim {
    /// <summary>
    /// Scene-side rig of the equipment icon studio (Assets/Scenes/EquipmentIconStudio.unity).
    /// Holds the camera, the stage anchor the garment meshes are spawned on, and the framing
    /// settings used by the editor generator (Tools ▸ The BROZ ▸ APS2 ▸ Generate Equipment Icons).
    /// Editor-only workflow — the scene is never shipped in a build.
    /// </summary>
    public class EquipmentIconStudio : MonoBehaviour {
        [Header("Rig")]
        public Camera renderCamera;

        public Transform stage;

        [Header("Framing")]
        [Tooltip("World rotation applied to the camera before framing (pitch/yaw of the icon viewpoint).")]
        public Vector3 viewEuler = new Vector3(12f, 205f, 0f);

        [Tooltip("Margin multiplier around the mesh bounds (1 = tight fit).")]
        public float padding = 1.1f;

        [Header("Output")]
        public int resolution = 512;
    }
}
