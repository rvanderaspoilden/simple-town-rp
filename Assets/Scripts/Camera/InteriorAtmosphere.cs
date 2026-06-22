using UnityEngine;

namespace Sim {
    /// <summary>
    /// Seals the interior: while the local player is inside a building (any <see cref="Roof"/>
    /// covering them), this publishes the room's world bounds and a cosy backdrop colour as
    /// global shader uniforms. The <c>InteriorFogFeature</c> fullscreen pass then melts every
    /// pixel outside those bounds — exterior scenery, dynamic actors and the sky alike — into
    /// the backdrop, with a soft world-space falloff just past the walls.
    ///
    /// Detection is entirely <see cref="Roof"/>-driven (warehouse, apartments and any future
    /// in-place building all carry a Roof with a player trigger), so there is no per-building
    /// wiring. Lives on the (DontDestroyOnLoad) Camera Manager GameObject next to
    /// <see cref="CameraWallFader"/> — same lifetime, same global-uniform pattern.
    /// </summary>
    public class InteriorAtmosphere : MonoBehaviour {
        [Header("Look")]
        [Tooltip("Colour the exterior melts into when you are inside. A warm, dark backdrop keeps the cosy DA.")]
        [SerializeField] private Color backdropColor = new Color(0.09f, 0.07f, 0.06f, 1f);
        [Tooltip("Width of the soft fade band past the walls / ceiling, in metres. Larger = hazier, gentler edge.")]
        [SerializeField] private float softness = 3f;

        [Header("Bounds")]
        [Tooltip("Extra metres added around the room footprint so the wall line itself stays fully clear before the fog ramps in.")]
        [SerializeField] private float boundsPadding = 0.6f;
        [Tooltip("Extra metres added above the roof height before fragments overhead fade out.")]
        [SerializeField] private float ceilingPadding = 1f;

        [Header("Transition")]
        [Tooltip("Blend speed in units per second when entering / leaving a building (≈ 1 / seconds-to-full).")]
        [SerializeField] private float transitionSpeed = 2.5f;

        private static readonly int BlendId    = Shader.PropertyToID("_InteriorBlend");
        private static readonly int CenterId   = Shader.PropertyToID("_InteriorCenter");
        private static readonly int ExtentsId  = Shader.PropertyToID("_InteriorExtents");
        private static readonly int CeilingId  = Shader.PropertyToID("_InteriorCeilingY");
        private static readonly int SoftnessId = Shader.PropertyToID("_InteriorSoftness");
        private static readonly int ColorId    = Shader.PropertyToID("_InteriorColor");
        private static readonly int InvVPId    = Shader.PropertyToID("_InteriorInvVP");

        private float _blend;
        // Last known room bounds, kept so the fog can finish fading out using them even after
        // the player has already left the trigger (LocalInterior went null).
        private Vector2 _center;
        private Vector2 _extents;
        private float _ceilingY;

        private void Awake() {
            Shader.SetGlobalFloat(BlendId, 0f);
        }

        private void LateUpdate() {
            Roof interior = Roof.LocalInterior;

            if (interior != null) {
                Bounds fp = interior.GetXZFootprint();
                _center  = new Vector2(fp.center.x, fp.center.z);
                _extents = new Vector2(fp.extents.x + boundsPadding, fp.extents.z + boundsPadding);
                _ceilingY = interior.CeilingY + ceilingPadding;
            }

            float target = interior != null ? 1f : 0f;
            _blend = Mathf.MoveTowards(_blend, target, transitionSpeed * Time.deltaTime);

            Shader.SetGlobalFloat(BlendId, _blend);
            if (_blend <= 0.001f) return;

            Shader.SetGlobalVector(CenterId, new Vector4(_center.x, _center.y, 0f, 0f));
            Shader.SetGlobalVector(ExtentsId, new Vector4(_extents.x, _extents.y, 0f, 0f));
            Shader.SetGlobalFloat(CeilingId, _ceilingY);
            Shader.SetGlobalFloat(SoftnessId, softness);
            Shader.SetGlobalColor(ColorId, backdropColor);

            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
            if (cam != null) {
                // Push the inverse GPU view-projection ourselves so the fog shader can rebuild
                // world position from depth without relying on built-in matrices being bound
                // inside a fullscreen blit pass.
                Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
                Shader.SetGlobalMatrix(InvVPId, viewProj.inverse);
            }
        }

        private void OnDisable() {
            _blend = 0f;
            Shader.SetGlobalFloat(BlendId, 0f);
        }
    }
}
