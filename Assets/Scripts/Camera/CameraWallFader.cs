using System.Collections.Generic;
using UnityEngine;

namespace Sim {
    /// <summary>How an occluding wall reveals the target behind it.</summary>
    public enum FadeStyle {
        /// <summary>The whole occluding wall dissolves out.</summary>
        FullWall,
        /// <summary>Only a soft screen-space disc around the target opens up; the rest of the wall stays solid.</summary>
        VisionCircle
    }

    /// <summary>
    /// Generalises the <c>Roof</c> auto-hide idea to arbitrary walls: every frame it casts a
    /// thick ray from the rendering camera toward whatever the camera is following (the local
    /// player on foot, the vehicle while driving) and smoothly dissolves any wall renderer
    /// caught in between, instead of the old brutal renderer enable/disable.
    ///
    /// The dissolve is driven by the <c>Sim/WallDither</c> shader: a faithful URP/Lit forward
    /// pass with a screen-space ordered-dither clip on a <c>_Fade</c> property (0 = opaque,
    /// 1 = fully see-through). A wall is converted to a shared dither variant of its own
    /// material on first occlusion and its <c>_Fade</c> is lerped per-renderer via a
    /// MaterialPropertyBlock; once it has been fully visible again for a short grace period its
    /// original materials are restored, so idle walls keep their exact look.
    ///
    /// Lives on the (DontDestroyOnLoad) Camera Manager GameObject. Only the Wall layer is
    /// considered; paintable apartment walls (which own a <see cref="Sim.Building.Wall"/>
    /// component and their own visibility toggle) are skipped to avoid fighting that system.
    /// </summary>
    public class CameraWallFader : MonoBehaviour {
        [Header("Detection")]
        [Tooltip("Layers treated as occluding walls. Defaults to the \"Wall\" layer when left empty.")]
        [SerializeField] private LayerMask wallMask;
        [Tooltip("Radius of the cast from camera to target. A little thickness catches walls grazing the sightline and avoids edge flicker.")]
        [SerializeField] private float castRadius = 0.35f;
        [Tooltip("World-space height added to the target pivot so the sightline tests the visible body, not the feet.")]
        [SerializeField] private float targetHeight = 1.1f;
        [Tooltip("Seconds between two occlusion casts. 0 = every frame.")]
        [SerializeField] private float castInterval = 0f;

        [Header("Fade")]
        [Tooltip("Maximum dissolve applied to an occluding wall (1 = fully invisible).")]
        [SerializeField, Range(0f, 1f)] private float maxFade = 1f;
        [Tooltip("Dissolve-in speed in _Fade units per second.")]
        [SerializeField] private float fadeInSpeed = 7f;
        [Tooltip("Dissolve-out speed in _Fade units per second.")]
        [SerializeField] private float fadeOutSpeed = 5f;
        [Tooltip("How long a wall must stay fully visible before its original materials are restored.")]
        [SerializeField] private float restoreGrace = 0.4f;

        [Header("Click-through")]
        [Tooltip("When a wall is dissolved past this fraction it stops blocking interaction clicks (moved to the Ignore Raycast layer, which CameraManager's interaction mask excludes). 0 disables click-through.")]
        [SerializeField, Range(0f, 1f)] private float clickThroughThreshold = 0.5f;

        [Header("Style")]
        [Tooltip("FullWall: the whole occluding wall dissolves. VisionCircle: only a soft disc around the target opens up. Switchable at runtime to A/B compare.")]
        [SerializeField] private FadeStyle style = FadeStyle.VisionCircle;

        [Header("Vision circle (VisionCircle style)")]
        [Tooltip("Radius of the see-through disc around the target, in metres of world space (stays ~constant on screen as you zoom).")]
        [SerializeField] private float visionWorldRadius = 1.7f;
        [Tooltip("Soft rim of the disc as a fraction of its radius (0 = hard edge, 1 = fully feathered). Used by the wall vision circle AND the foliage fade.")]
        [SerializeField, Range(0f, 1f)] private float visionSoftness = 0.4f;

        [Header("Trees / foliage")]
        [Tooltip("Also dissolve tree leaves in front of the target, via the Sim/TreeVisionDither node in the Leaf shader graph. Uses the same vision disc as the wall VisionCircle style, independent of it.")]
        [SerializeField] private bool affectTrees = true;
        [Tooltip("Master strength of the foliage dissolve inside the disc (0 = off, 1 = full).")]
        [SerializeField, Range(0f, 1f)] private float treeFadeStrength = 1f;

        [Header("Shaders")]
        [SerializeField] private string fullWallShaderName = "Sim/WallDither";
        [SerializeField] private string visionCircleShaderName = "Sim/WallVisionCircle";

        private Shader _fullWallShader;
        private Shader _visionShader;
        private FadeStyle _appliedStyle;

        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int VisionCenterId = Shader.PropertyToID("_VisionCenter");
        private static readonly int VisionRadiusId = Shader.PropertyToID("_VisionRadius");
        private static readonly int VisionSoftnessId = Shader.PropertyToID("_VisionSoftness");
        private static readonly int VisionTargetDistId = Shader.PropertyToID("_VisionTargetDist");
        private static readonly int VisionTreeStrengthId = Shader.PropertyToID("_VisionTreeStrength");

        private Shader ActiveShader => style == FadeStyle.VisionCircle ? _visionShader : _fullWallShader;

        // One shared dither material per source material (carries that material's maps/keywords).
        private readonly Dictionary<Material, Material> _ditherBySource = new Dictionary<Material, Material>();

        private sealed class FadeState {
            public Renderer renderer;
            public Material[] originalMaterials;
            public MaterialPropertyBlock block;
            public float fade;
            public bool occluding;       // set true each tick it blocks the sightline
            public float visibleTimer;   // seconds spent fully visible (fade==0 & !occluding)
            public int originalLayer = -1; // layer to restore (captured before any click-through swap)
            public bool clickThrough;      // currently parked on the Ignore Raycast layer
        }

        private readonly Dictionary<Renderer, FadeState> _states = new Dictionary<Renderer, FadeState>();
        private readonly List<Renderer> _scratchRemove = new List<Renderer>();
        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private float _castTimer;

        private const int IgnoreRaycastLayer = 2; // built-in "Ignore Raycast"
        private int _detectMask;                  // wallMask + Ignore Raycast (to keep finding parked walls)

        private void Awake() {
            if (wallMask == 0) {
                int wall = LayerMask.NameToLayer("Wall");
                if (wall >= 0) wallMask = 1 << wall;
            }
            _detectMask = wallMask | (1 << IgnoreRaycastLayer);
            _fullWallShader = Shader.Find(fullWallShaderName);
            _visionShader = Shader.Find(visionCircleShaderName);
            if (_fullWallShader == null || _visionShader == null) {
                Debug.LogError($"[CameraWallFader] Shader(s) not found ('{fullWallShaderName}', '{visionCircleShaderName}'). Component disabled.");
                enabled = false;
                return;
            }
            _appliedStyle = style;
        }

        private void LateUpdate() {
            float dt = Time.deltaTime;

            // Allow flipping the style in the inspector at runtime to compare: re-convert walls
            // with the newly selected shader.
            if (style != _appliedStyle) {
                RestoreAll();
                _ditherBySource.Clear();
                _appliedStyle = style;
            }

            _castTimer -= dt;
            if (_castTimer <= 0f) {
                _castTimer = castInterval;
                UpdateOccluders();
            }

            // The disc globals drive both the wall VisionCircle shader and the foliage fade, so
            // publish them every frame regardless of the wall style.
            UpdateVisionGlobals();

            ApplyFades(dt);
        }

        // Per-frame global uniforms describing the see-through disc around the target, consumed by
        // Sim/WallVisionCircle (walls) and Sim/TreeVisionDither (foliage). Radius 0 / strength 0
        // disables the effect (no target / behind camera).
        private void UpdateVisionGlobals() {
            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
            Transform target = ResolveTarget();
            if (cam == null || target == null) { DisableVision(); return; }

            Vector3 camPos = cam.transform.position;
            Vector3 targetPos = target.position + Vector3.up * targetHeight;
            Vector3 c = cam.WorldToScreenPoint(targetPos);
            if (c.z <= 0f) { DisableVision(); return; }

            Vector3 e = cam.WorldToScreenPoint(targetPos + cam.transform.up * visionWorldRadius);
            float radiusPixels = Vector2.Distance(new Vector2(c.x, c.y), new Vector2(e.x, e.y));
            float h = Mathf.Max(cam.pixelHeight, 1);

            Shader.SetGlobalVector(VisionCenterId, new Vector4(c.x / Mathf.Max(cam.pixelWidth, 1), c.y / h, 0f, 0f));
            Shader.SetGlobalFloat(VisionRadiusId, radiusPixels / h);
            Shader.SetGlobalFloat(VisionSoftnessId, visionSoftness);
            Shader.SetGlobalFloat(VisionTargetDistId, Vector3.Distance(camPos, targetPos));
            Shader.SetGlobalFloat(VisionTreeStrengthId, affectTrees ? treeFadeStrength : 0f);
        }

        private void DisableVision() {
            Shader.SetGlobalFloat(VisionRadiusId, 0f);
            Shader.SetGlobalFloat(VisionTreeStrengthId, 0f);
        }

        private void UpdateOccluders() {
            // Clear last tick's votes.
            foreach (FadeState s in _states.Values) s.occluding = false;

            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
            Transform target = ResolveTarget();
            if (cam == null || target == null) return;

            Vector3 camPos = cam.transform.position;
            Vector3 targetPos = target.position + Vector3.up * targetHeight;
            Vector3 delta = targetPos - camPos;
            float dist = delta.magnitude;
            if (dist <= 0.01f) return;

            Vector3 dir = delta / dist;
            // SphereCast.maxDistance is the sweep of the sphere CENTRE — the leading edge then
            // reaches camPos + dir*(maxDistance + castRadius). To stop the sweep AT the target
            // plane (so a wall touching the player's back isn't grabbed as an occluder), we
            // shrink the sweep distance by the radius.
            float maxCastDist = Mathf.Max(0.01f, dist - castRadius);
            int count = Physics.SphereCastNonAlloc(camPos, castRadius, dir, _hits, maxCastDist, _detectMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) {
                Collider col = _hits[i].collider;
                Renderer r = ResolveWallRenderer(col);
                if (r == null) continue;
                // Only adopt NEW occluders from the real wall layer(s). Colliders we parked on
                // Ignore Raycast for click-through are accepted only because we already track them,
                // so unrelated Ignore-Raycast colliders on the sightline are never grabbed.
                bool onWallLayer = (wallMask.value & (1 << col.gameObject.layer)) != 0;
                if (!onWallLayer && !_states.ContainsKey(r)) continue;
                GetOrCreateState(r).occluding = true;
            }
        }

        private void ApplyFades(float dt) {
            _scratchRemove.Clear();

            foreach (KeyValuePair<Renderer, FadeState> kv in _states) {
                FadeState s = kv.Value;
                if (s.renderer == null) { _scratchRemove.Add(kv.Key); continue; }

                float goal = s.occluding ? maxFade : 0f;
                float speed = s.occluding ? fadeInSpeed : fadeOutSpeed;
                s.fade = Mathf.MoveTowards(s.fade, goal, speed * dt);

                UpdateClickThrough(s);

                if (s.fade > 0.0001f) {
                    EnsureConverted(s);
                    s.renderer.GetPropertyBlock(s.block);
                    s.block.SetFloat(FadeId, s.fade);
                    s.renderer.SetPropertyBlock(s.block);
                    s.visibleTimer = 0f;
                } else {
                    // Fully visible: clear the override and, after a grace period, restore originals.
                    if (s.block != null) {
                        s.renderer.GetPropertyBlock(s.block);
                        s.block.SetFloat(FadeId, 0f);
                        s.renderer.SetPropertyBlock(s.block);
                    }
                    if (!s.occluding) {
                        s.visibleTimer += dt;
                        if (s.visibleTimer >= restoreGrace) {
                            Restore(s);
                            _scratchRemove.Add(kv.Key);
                        }
                    }
                }
            }

            for (int i = 0; i < _scratchRemove.Count; i++) _states.Remove(_scratchRemove[i]);
        }

        private FadeState GetOrCreateState(Renderer r) {
            if (!_states.TryGetValue(r, out FadeState s)) {
                s = new FadeState { renderer = r, block = new MaterialPropertyBlock() };
                _states.Add(r, s);
            }
            return s;
        }

        // Once a wall is dissolved past clickThroughThreshold, park it on the Ignore Raycast layer
        // so interaction clicks/hover pass through to whatever is behind it; restore on the way back.
        private void UpdateClickThrough(FadeState s) {
            if (clickThroughThreshold <= 0f || s.renderer == null) return;
            bool wantThrough = s.fade >= clickThroughThreshold;
            if (wantThrough == s.clickThrough) return;
            s.clickThrough = wantThrough;
            GameObject go = s.renderer.gameObject;
            if (wantThrough) {
                if (s.originalLayer < 0) s.originalLayer = go.layer;
                go.layer = IgnoreRaycastLayer;
            } else if (s.originalLayer >= 0) {
                go.layer = s.originalLayer;
            }
        }

        // Swap the renderer's materials to shared dither variants (cached per source material),
        // remembering the originals so we can restore them once the wall is visible again.
        private void EnsureConverted(FadeState s) {
            if (s.originalMaterials != null) return;

            if (s.originalLayer < 0) s.originalLayer = s.renderer.gameObject.layer;
            Material[] src = s.renderer.sharedMaterials;
            s.originalMaterials = src;
            Material[] dith = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) dith[i] = GetDitherVariant(src[i]);
            s.renderer.sharedMaterials = dith;
        }

        private void Restore(FadeState s) {
            if (s.renderer != null) {
                if (s.originalMaterials != null) s.renderer.sharedMaterials = s.originalMaterials;
                if (s.originalLayer >= 0) s.renderer.gameObject.layer = s.originalLayer;
            }
            s.originalMaterials = null;
            s.originalLayer = -1;
            s.clickThrough = false;
        }

        private Material GetDitherVariant(Material source) {
            if (source == null) return null;
            if (_ditherBySource.TryGetValue(source, out Material cached)) return cached;

            Material m = new Material(ActiveShader) { name = source.name + " (" + ActiveShader.name + ")" };

            if (source.HasProperty("_BaseMap")) {
                m.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                m.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                m.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
            } else if (source.HasProperty("_MainTex")) {
                m.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                m.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
                m.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
            }

            if (source.HasProperty("_BaseColor")) m.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            else if (source.HasProperty("_Color")) m.SetColor("_BaseColor", source.GetColor("_Color"));

            if (source.HasProperty("_BumpMap")) {
                Texture bump = source.GetTexture("_BumpMap");
                if (bump != null) {
                    m.SetTexture("_BumpMap", bump);
                    m.SetFloat("_BumpScale", source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
            }

            if (source.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
            if (source.HasProperty("_Metallic")) m.SetFloat("_Metallic", source.GetFloat("_Metallic"));

            _ditherBySource.Add(source, m);
            return m;
        }

        private Transform ResolveTarget() {
            PlayerController p = PlayerController.Local;
            if (p == null) return null;
            if ((p.IsDriving || p.IsPassenger) && p.CurrentVehicle != null) return p.CurrentVehicle.transform;
            return p.transform;
        }

        // Maps a hit collider to the wall renderer to fade, skipping paintable apartment walls
        // (they own a Wall component and their own visibility toggle).
        private Renderer ResolveWallRenderer(Collider col) {
            if (col == null) return null;
            if (col.GetComponentInParent<Sim.Building.Wall>() != null) return null;
            Renderer r = col.GetComponent<Renderer>();
            if (r == null) r = col.GetComponentInParent<Renderer>();
            return r;
        }

        private void RestoreAll() {
            foreach (FadeState s in _states.Values) Restore(s);
            _states.Clear();
        }

        private void OnDisable() {
            // Restore everything so walls don't get stuck dissolved when the component is turned off.
            RestoreAll();
            DisableVision();
        }
    }
}
