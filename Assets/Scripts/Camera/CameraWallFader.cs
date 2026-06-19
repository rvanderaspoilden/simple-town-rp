using System.Collections.Generic;
using UnityEngine;

namespace Sim {
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

        [Header("Shader")]
        [SerializeField] private string ditherShaderName = "Sim/WallDither";

        private Shader _ditherShader;
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        // One shared dither material per source material (carries that material's maps/keywords).
        private readonly Dictionary<Material, Material> _ditherBySource = new Dictionary<Material, Material>();

        private sealed class FadeState {
            public Renderer renderer;
            public Material[] originalMaterials;
            public MaterialPropertyBlock block;
            public float fade;
            public bool occluding;       // set true each tick it blocks the sightline
            public float visibleTimer;   // seconds spent fully visible (fade==0 & !occluding)
        }

        private readonly Dictionary<Renderer, FadeState> _states = new Dictionary<Renderer, FadeState>();
        private readonly List<Renderer> _scratchRemove = new List<Renderer>();
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private float _castTimer;

        private void Awake() {
            if (wallMask == 0) {
                int wall = LayerMask.NameToLayer("Wall");
                if (wall >= 0) wallMask = 1 << wall;
            }
            _ditherShader = Shader.Find(ditherShaderName);
            if (_ditherShader == null) {
                Debug.LogError($"[CameraWallFader] Shader '{ditherShaderName}' not found. Component disabled.");
                enabled = false;
            }
        }

        private void LateUpdate() {
            float dt = Time.deltaTime;

            _castTimer -= dt;
            if (_castTimer <= 0f) {
                _castTimer = castInterval;
                UpdateOccluders();
            }

            ApplyFades(dt);
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
            int count = Physics.SphereCastNonAlloc(camPos, castRadius, dir, _hits, dist, wallMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) {
                Renderer r = ResolveWallRenderer(_hits[i].collider);
                if (r == null) continue;
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

        // Swap the renderer's materials to shared dither variants (cached per source material),
        // remembering the originals so we can restore them once the wall is visible again.
        private void EnsureConverted(FadeState s) {
            if (s.originalMaterials != null) return;

            Material[] src = s.renderer.sharedMaterials;
            s.originalMaterials = src;
            Material[] dith = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) dith[i] = GetDitherVariant(src[i]);
            s.renderer.sharedMaterials = dith;
        }

        private void Restore(FadeState s) {
            if (s.originalMaterials != null && s.renderer != null) {
                s.renderer.sharedMaterials = s.originalMaterials;
            }
            s.originalMaterials = null;
        }

        private Material GetDitherVariant(Material source) {
            if (source == null) return null;
            if (_ditherBySource.TryGetValue(source, out Material cached)) return cached;

            Material m = new Material(_ditherShader) { name = source.name + " (WallDither)" };

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

        private void OnDisable() {
            // Restore everything so walls don't get stuck dissolved when the component is turned off.
            foreach (FadeState s in _states.Values) Restore(s);
            _states.Clear();
        }
    }
}
