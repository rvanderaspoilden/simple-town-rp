using UnityEngine;

namespace Sim {
    /// <summary>
    /// Drives the "window as portal" effect: a dedicated camera that shares the gameplay camera's
    /// exact viewpoint but renders ONLY the Exterior-layer diorama into a screen-sized RenderTexture
    /// (published as the global <c>_ExteriorRT</c>). The <c>Sim/WindowPortal</c> glass shader then
    /// samples that RT in screen space, so the 3D exterior appears inside each window with true
    /// parallax and correct frame occlusion — and stays completely isolated from the interior fog.
    ///
    /// Lives on the (DontDestroyOnLoad) Camera Manager next to <see cref="CameraWallFader"/>. The
    /// portal camera + RT are created at runtime, so nothing extra needs authoring in the prefab.
    /// Active only while the local player is inside a building (<see cref="Roof.LocalInterior"/>),
    /// so the open city pays nothing.
    /// </summary>
    public class ExteriorPortalCamera : MonoBehaviour {
        [Header("Setup")]
        [Tooltip("Layer that holds the exterior diorama geometry. Rendered ONLY by the portal camera, never by the gameplay camera.")]
        [SerializeField] private string exteriorLayerName = "Exterior";
        [Tooltip("RenderTexture scale relative to screen resolution (1 = full res). Lower it to save fill-rate.")]
        [SerializeField, Range(0.25f, 1f)] private float renderScale = 1f;

        [Header("Time-of-day exterior lighting (driven by TimeManager, coherent with MeteoManager)")]
        [Tooltip("Sky/ambient colour around midday.")]
        [SerializeField] private Color daySky   = new Color(0.42f, 0.56f, 0.78f, 1f);
        [Tooltip("Warm sky/ambient colour at dawn & dusk (low sun).")]
        [SerializeField] private Color duskSky  = new Color(0.80f, 0.45f, 0.32f, 1f);
        [Tooltip("Sky/ambient colour in the dead of night.")]
        [SerializeField] private Color nightSky = new Color(0.05f, 0.06f, 0.12f, 1f);
        [Tooltip("Directional sun colour/intensity at midday (HDR).")]
        [SerializeField, ColorUsage(false, true)] private Color daySun  = new Color(1.0f, 0.96f, 0.86f, 1f);
        [Tooltip("Directional sun colour/intensity when the sun is near the horizon (HDR).")]
        [SerializeField, ColorUsage(false, true)] private Color duskSun = new Color(1.0f, 0.55f, 0.30f, 1f);

        private Camera _portalCam;
        private RenderTexture _rt;
        private int _exteriorLayer = -1;
        private int _exteriorMask;
        private int _rtW, _rtH;

        private static readonly int ExteriorRTId   = Shader.PropertyToID("_ExteriorRT");
        private static readonly int PortalActiveId  = Shader.PropertyToID("_PortalActive");
        private static readonly int ExtSunDirId     = Shader.PropertyToID("_ExtSunDir");
        private static readonly int ExtSunColorId   = Shader.PropertyToID("_ExtSunColor");
        private static readonly int ExtSkyColorId   = Shader.PropertyToID("_ExtSkyColor");
        private static readonly int ExtWindowLitId  = Shader.PropertyToID("_ExtWindowLit");
        private static readonly int ExtActiveId     = Shader.PropertyToID("_ExtActive");

        private Color _currentSky = new Color(0.05f, 0.06f, 0.12f, 1f);

        private void Awake() {
            _exteriorLayer = LayerMask.NameToLayer(exteriorLayerName);
            if (_exteriorLayer < 0) {
                Debug.LogError($"[ExteriorPortalCamera] Layer '{exteriorLayerName}' is missing. Component disabled.");
                enabled = false;
                return;
            }
            _exteriorMask = 1 << _exteriorLayer;
            CreatePortalCamera();
        }

        private void CreatePortalCamera() {
            var go = new GameObject("ExteriorPortalCam");
            go.transform.SetParent(transform, false);
            _portalCam = go.AddComponent<Camera>();
            _portalCam.cullingMask = _exteriorMask;
            _portalCam.clearFlags = CameraClearFlags.SolidColor;
            _portalCam.backgroundColor = _currentSky;
            _portalCam.allowMSAA = false;
            _portalCam.allowHDR = true;
            _portalCam.useOcclusionCulling = false;
            _portalCam.enabled = false; // enabled on demand while inside a building
        }

        private void EnsureRenderTexture() {
            int w = Mathf.Max(8, Mathf.RoundToInt(Screen.width  * renderScale));
            int h = Mathf.Max(8, Mathf.RoundToInt(Screen.height * renderScale));
            if (_rt != null && _rtW == w && _rtH == h) return;

            ReleaseRenderTexture();
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "ExteriorPortalRT" };
            _rt.Create();
            _rtW = w; _rtH = h;
            _portalCam.targetTexture = _rt;
            Shader.SetGlobalTexture(ExteriorRTId, _rt);
        }

        private void ReleaseRenderTexture() {
            if (_rt == null) return;
            if (_portalCam != null) _portalCam.targetTexture = null;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }

        private void LateUpdate() {
            Camera main = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
            if (main == null) return;

            // The Exterior diorama lives in the offset "void" where halls are instantiated and must
            // NEVER be drawn directly — only seen through a window via the RT. Strip the bit from every
            // active camera except the portal (gameplay, build, drive, minimap, scene view…) each frame
            // so no stray camera reveals the floating diorama boxes.
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++) {
                if (cams[i] != _portalCam) cams[i].cullingMask &= ~_exteriorMask;
            }

            // Active while the local player is inside any void interior floor (corridor OR an
            // apartment on it — one room per floor, so walking apartment↔corridor keeps the
            // portal on). "city" and "no room yet" pay nothing. The Roof trigger only covers the
            // apartment subspaces, not the hall corridor, so it can't gate this on its own.
            string room = ClientPropManager.Instance != null ? ClientPropManager.Instance.CurrentRoomId : null;
            bool active = !string.IsNullOrEmpty(room) && room != "city";
            if (!active) {
                if (_portalCam.enabled) _portalCam.enabled = false;
                Shader.SetGlobalFloat(PortalActiveId, 0f);
                Shader.SetGlobalFloat(ExtActiveId, 0f);
                return;
            }

            EnsureRenderTexture();
            PushTimeOfDayLighting();

            // Clone the gameplay camera viewpoint exactly → screen-space RT sampling lines the
            // exterior up inside the window with genuine parallax.
            _portalCam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            _portalCam.fieldOfView   = main.fieldOfView;
            _portalCam.nearClipPlane = main.nearClipPlane;
            _portalCam.farClipPlane  = main.farClipPlane;
            _portalCam.backgroundColor = _currentSky;
            _portalCam.enabled = true;

            Shader.SetGlobalFloat(PortalActiveId, 1f);
        }

        /// <summary>
        /// Derives the exterior sun direction + day/night palette from the shared in-game clock
        /// (<see cref="TimeManager.CurrentTime"/> — the same source <see cref="MeteoManager"/> drives
        /// the real sky from) and publishes them as globals the <c>Sim/DioramaBuilding</c> shader reads,
        /// so the city seen through the windows matches the current time of day. Works indoors regardless
        /// of the city sun being toggled off, because nothing here depends on the live scene light.
        /// </summary>
        private void PushTimeOfDayLighting() {
            float hour = (float)(TimeManager.CurrentTime.TotalHours % 24.0);
            if (hour < 0f) hour += 24f;

            // Sun elevation proxy: +1 at noon, 0 at 06:00/18:00, -1 at midnight.
            float elev = Mathf.Cos((hour - 12f) / 24f * (2f * Mathf.PI));
            // Day master (full day high noon, full night after dusk, soft twilight either side).
            float dayFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, 0.45f, elev));
            // Warm horizon tint, strongest when the sun sits near the horizon (dawn/dusk).
            float twilight = Mathf.Clamp01(1f - Mathf.Abs(elev) / 0.35f);

            Color sky = Color.Lerp(nightSky, daySky, dayFactor);
            sky = Color.Lerp(sky, duskSky, twilight * (1f - dayFactor * 0.5f));
            _currentSky = sky;

            float sunUp = Mathf.Clamp01(elev * 2f + 0.1f); // sun only contributes while above the horizon
            Color sun = Color.Lerp(duskSun, daySun, dayFactor) * sunUp;

            float windowLit = Mathf.Clamp01(1f - dayFactor * 1.5f); // lit at night/dusk, dark by mid-morning

            // Sun sweep: low east at 06:00 → overhead at noon → low west at 18:00.
            float ang = (hour - 6f) / 12f * Mathf.PI;
            Vector3 sunDir = new Vector3(0.3f, Mathf.Sin(ang), Mathf.Cos(ang)).normalized;

            Shader.SetGlobalVector(ExtSunDirId, sunDir);
            Shader.SetGlobalColor(ExtSunColorId, sun);
            Shader.SetGlobalColor(ExtSkyColorId, sky);
            Shader.SetGlobalFloat(ExtWindowLitId, windowLit);
            Shader.SetGlobalFloat(ExtActiveId, 1f);
        }

        private void OnDisable() {
            Shader.SetGlobalFloat(PortalActiveId, 0f);
            Shader.SetGlobalFloat(ExtActiveId, 0f);
            if (_portalCam != null) _portalCam.enabled = false;
        }

        private void OnDestroy() {
            ReleaseRenderTexture();
            if (_portalCam != null) Destroy(_portalCam.gameObject);
        }
    }
}
