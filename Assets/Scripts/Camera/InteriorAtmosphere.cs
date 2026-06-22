using UnityEngine;

namespace Sim {
    /// <summary>
    /// Seals the interior: while the local player is inside a building (any <see cref="Roof"/>
    /// covering them) AND the camera sits above the roof line, this publishes the room's world
    /// bounds and a cosy backdrop colour as global shader uniforms. The <c>InteriorFogFeature</c>
    /// fullscreen pass then melts every pixel outside the room footprint — exterior scenery,
    /// dynamic actors and the sky alike — into the backdrop, with a soft falloff past the walls.
    ///
    /// The fog is gated on camera height: it ramps in only as the camera climbs through a few-unit
    /// band at the roof height (<see cref="roofFogHeight"/>). So the normal top-down gameplay view
    /// is sealed, but zooming the camera down to an eye-level view below the roof fades the fog out
    /// and reveals the exterior through doors / windows naturally — no per-pixel sightline tricks.
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
        [Tooltip("Width of the soft fade band past the walls, in metres. Larger = hazier, gentler edge.")]
        [SerializeField] private float softness = 3f;

        [Header("Bounds")]
        [Tooltip("Extra metres added around the room footprint so the wall line itself stays fully clear before the fog ramps in.")]
        [SerializeField] private float boundsPadding = 0.6f;

        [Header("Camera-height gate")]
        [Tooltip("Height band (metres) above the roof over which the fog ramps from off to full as the camera rises. The fog is off while the camera is at or below the roof line, full once it is this many metres above it.")]
        [SerializeField] private float roofFogHeight = 2.5f;

        [Header("Transition")]
        [Tooltip("Blend speed in units per second when entering / leaving a building (≈ 1 / seconds-to-full).")]
        [SerializeField] private float transitionSpeed = 2.5f;

        private static readonly int BlendId    = Shader.PropertyToID("_InteriorBlend");
        private static readonly int CenterId   = Shader.PropertyToID("_InteriorCenter");
        private static readonly int ExtentsId  = Shader.PropertyToID("_InteriorExtents");
        private static readonly int SoftnessId = Shader.PropertyToID("_InteriorSoftness");
        private static readonly int ColorId    = Shader.PropertyToID("_InteriorColor");
        private static readonly int InvVPId    = Shader.PropertyToID("_InteriorInvVP");

        private float _blend;
        // Last known room bounds, kept so the fog can finish fading out using them even after
        // the player has already left the trigger (LocalInterior went null).
        private Vector2 _center;
        private Vector2 _extents;
        private float _roofY;

        private void Awake() {
            Shader.SetGlobalFloat(BlendId, 0f);
        }

        private void LateUpdate() {
            Roof interior = Roof.LocalInterior;

            if (interior != null) {
                Bounds fp = interior.GetXZFootprint();
                _center  = new Vector2(fp.center.x, fp.center.z);
                _extents = new Vector2(fp.extents.x + boundsPadding, fp.extents.z + boundsPadding);
                _roofY   = interior.CeilingY;
            }

            float enterTarget = interior != null ? 1f : 0f;
            _blend = Mathf.MoveTowards(_blend, enterTarget, transitionSpeed * Time.deltaTime);

            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;

            // Camera-height gate: fog only seals the interior once the camera climbs above the
            // roof line. Below it (eye-level / zoomed-in view) the fog fades out so windows and
            // doorways read naturally.
            float camFactor = 1f;
            if (cam != null) {
                float t = Mathf.InverseLerp(_roofY, _roofY + Mathf.Max(roofFogHeight, 0.01f), cam.transform.position.y);
                camFactor = Mathf.SmoothStep(0f, 1f, t);
            }

            float effective = _blend * camFactor;
            Shader.SetGlobalFloat(BlendId, effective);
            if (effective <= 0.001f) return;

            Shader.SetGlobalVector(CenterId, new Vector4(_center.x, _center.y, 0f, 0f));
            Shader.SetGlobalVector(ExtentsId, new Vector4(_extents.x, _extents.y, 0f, 0f));
            Shader.SetGlobalFloat(SoftnessId, softness);
            Shader.SetGlobalColor(ColorId, backdropColor);

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
