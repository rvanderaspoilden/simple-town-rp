using System.Collections.Generic;
using Sim;
using UnityEngine;

/// <summary>
/// Visibility controller for a building's roof.
///
/// Decoupled from any specific trigger source. Other components (the built-in
/// interior trigger on this same GameObject, doors, windows, balconies, …) call
/// <see cref="AddHider"/> / <see cref="RemoveHider"/> to vote the roof hidden.
/// The roof is hidden iff at least one source is active.
///
/// A static registry of live instances is exposed so external triggers can resolve
/// the right roof by spatial query (client-side props are not parented under their
/// apartment, so a hierarchy-based lookup wouldn't work).
/// </summary>
[RequireComponent(typeof(Collider))]
public class Roof : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private List<MeshRenderer> renderersToHide;

    [SerializeField]
    private GameObject preventClickChild;

    [Tooltip("When true, the minimap fades out while the local player stands under this roof. Disable for canopies/awnings that should not be treated as interiors.")]
    [SerializeField]
    private bool hideMinimap = true;

    [Tooltip("Optional manual interior ceiling height (world Y) for the InteriorAtmosphere fog's camera-height gate. Leave negative to auto-compute from the top of the hidden renderers. Set it on tall buildings whose hidden shell rises far above the walkable floor (e.g. the multi-storey hotel) so the fog keys to the real room ceiling instead of the building top.")]
    [SerializeField]
    private float interiorCeilingYOverride = -1f;

    [Header("Fade")]
    [Tooltip("Dissolve-in speed in _Fade units/sec when the roof is hidden (player steps inside).")]
    [SerializeField] private float hideFadeSpeed = 6f;
    [Tooltip("Dissolve-out (reveal) speed in _Fade units/sec when the roof becomes visible again.")]
    [SerializeField] private float showFadeSpeed = 6f;
    [Tooltip("Screen-space dither dissolve shader used to fade the roof in/out. Falls back to a hard enable/disable toggle if not found.")]
    [SerializeField] private string ditherShaderName = "Sim/WallDither";

    private readonly HashSet<object> _hiders = new HashSet<object>();

    // ── Fade state ────────────────────────────────────────────────────────────
    // The roof no longer pops in/out: instead of toggling Renderer.enabled it dissolves
    // through the Sim/WallDither shader (_Fade 0 = solid, 1 = fully see-through). Each
    // renderer is swapped to a dither variant of its own material only while a transition
    // is running, and restored to the original once fully visible, so an idle-visible roof
    // keeps its exact look. A fully-hidden roof disables its renderers (zero draw cost,
    // same as the old behaviour) once the dissolve completes.
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private Shader _ditherShader;
    private MaterialPropertyBlock _fadeBlock;
    private FadeSlot[] _slots;
    private float _fade;        // 0 = fully visible, 1 = fully hidden (dissolved out)
    private float _targetFade;  // 0 or 1
    private bool _fading;

    private sealed class FadeSlot {
        public MeshRenderer renderer;
        public Material[] original; // captured on first conversion, restored when fully visible
        public Material[] dither;   // cached dither variants of the originals
        public bool converted;      // renderer currently showing its dither materials
    }

    // Tracks whether the local player is currently inside this specific roof's
    // trigger, so we add/remove our contribution to the global minimap-hider
    // counter exactly once.
    private bool _coveringMinimap;

    // ── Minimap coverage (global) ─────────────────────────────────────────────
    // Counter incremented by every Roof that currently covers the local player
    // AND has hideMinimap == true. The event fires only on 0↔1 transitions, so
    // the MinimapController only fades once even with overlapping roofs.
    private static int _minimapHiderCount;
    public  static bool IsMinimapCovered => _minimapHiderCount > 0;
    public  static event System.Action<bool> OnMinimapCoverageChanged;

    // ── Local interior (interior atmosphere) ──────────────────────────────────
    // Roofs (real interiors only — hideMinimap == true) currently covering the local
    // player. LocalInterior exposes the innermost one (smallest XZ footprint), so the
    // InteriorAtmosphere fog seals the smallest enclosing room. Driven off the same
    // player trigger as the minimap coverage above.
    private static readonly HashSet<Roof> _localInteriorRoofs = new HashSet<Roof>();
    public  static Roof LocalInterior { get; private set; }
    public  static event System.Action<Roof> OnLocalInteriorChanged;

    // Keys for the built-in interior trigger on this GameObject.
    private static readonly object PlayerKey = new object();
    private static readonly object CameraKey = new object();

    // ── Static registry ───────────────────────────────────────────────────────

    private static readonly HashSet<Roof> _instances = new HashSet<Roof>();
    public static IReadOnlyCollection<Roof> All => _instances;

    private void OnEnable() => _instances.Add(this);
    // OnDisable both clears interior occupants and unregisters from the registry,
    // so a deactivated roof isn't returned to spatial lookups.

    /// <summary>
    /// World-space XZ footprint of this roof's visual extent. Used by external
    /// triggers (doors, windows) to match themselves to the right apartment when
    /// they are not parented under it. Y is intentionally ignored.
    /// </summary>
    public Bounds GetXZFootprint() {
        if (renderersToHide == null || renderersToHide.Count == 0) {
            // Fallback: this object's own collider bounds.
            Collider c = GetComponent<Collider>();
            return c != null ? c.bounds : new Bounds(transform.position, Vector3.zero);
        }
        Bounds b = renderersToHide[0].bounds;
        for (int i = 1; i < renderersToHide.Count; i++) {
            if (renderersToHide[i] != null) b.Encapsulate(renderersToHide[i].bounds);
        }
        return b;
    }

    /// <summary>
    /// World-space interior ceiling height used by the InteriorAtmosphere fog's camera-height gate.
    /// Defaults to the top of this roof's hidden renderers, but can be overridden per building for
    /// tall shells whose hidden geometry rises far above the walkable floor (see
    /// <see cref="interiorCeilingYOverride"/>).
    /// </summary>
    public float CeilingY => interiorCeilingYOverride >= 0f ? interiorCeilingYOverride : GetXZFootprint().max.y;

    private void Awake() {
        _fadeBlock = new MaterialPropertyBlock();
        _ditherShader = Shader.Find(ditherShaderName);

        _slots = new FadeSlot[this.renderersToHide.Count];
        for (int i = 0; i < this.renderersToHide.Count; i++) {
            MeshRenderer r = this.renderersToHide[i];
            // Own material instance per roof so each one fades (and restores) independently.
            if (r != null) r.material = new Material(r.material);
            _slots[i] = new FadeSlot { renderer = r };
        }

        if (this.preventClickChild != null) this.preventClickChild.SetActive(true);
    }

    /// <summary>Register a source that wants the roof hidden. Idempotent per source.</summary>
    public void AddHider(object source) {
        if (source == null) return;
        if (_hiders.Add(source)) Refresh();
    }

    /// <summary>Release a previously-added source.</summary>
    public void RemoveHider(object source) {
        if (source == null) return;
        if (_hiders.Remove(source)) Refresh();
    }

    private void OnTriggerStay(Collider other) {
        // Per-fixed-frame: only the idempotent AddHider votes here (pre-existing
        // behaviour, kept defensive for late-spawned local players). The minimap
        // coverage signal lives on Enter/Exit so the global counter doesn't pay
        // any per-frame cost.
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            AddHider(PlayerKey);
            AddLocalInterior(); // defensive, for late-spawned/teleported-in local players
        } else if (other.CompareTag("MainCamera")) {
            AddHider(CameraKey);
        }
    }

    private void OnTriggerEnter(Collider other) {
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            SetMinimapCoverage(true);
            AddLocalInterior();
        }
    }

    private void OnTriggerExit(Collider other) {
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            RemoveHider(PlayerKey);
            SetMinimapCoverage(false);
            RemoveLocalInterior();
        } else if (other.CompareTag("MainCamera")) {
            RemoveHider(CameraKey);
        }
    }

    private void OnDisable() {
        _instances.Remove(this);
        // Drop interior occupants so a re-enabled roof starts visible.
        _hiders.Remove(PlayerKey);
        _hiders.Remove(CameraKey);
        // Snap (no animation — Update won't run while disabled) so a re-enabled roof is
        // already in the correct visible/hidden state.
        SnapToState();
        SetMinimapCoverage(false);
        RemoveLocalInterior();
    }

    // ── Local interior selection ──────────────────────────────────────────────

    private void AddLocalInterior() {
        if (!hideMinimap) return; // canopies/awnings opt out of being treated as interiors
        if (_localInteriorRoofs.Add(this)) RecomputeLocalInterior();
    }

    private void RemoveLocalInterior() {
        if (_localInteriorRoofs.Remove(this)) RecomputeLocalInterior();
    }

    // Innermost (smallest XZ footprint) wins so overlapping roofs seal the tightest room.
    private static void RecomputeLocalInterior() {
        Roof best = null;
        float bestArea = float.MaxValue;
        foreach (Roof r in _localInteriorRoofs) {
            if (r == null) continue;
            Bounds b = r.GetXZFootprint();
            float area = b.size.x * b.size.z;
            if (area < bestArea) { bestArea = area; best = r; }
        }
        if (best != LocalInterior) {
            LocalInterior = best;
            OnLocalInteriorChanged?.Invoke(best);
        }
    }

    private void SetMinimapCoverage(bool covering) {
        if (_coveringMinimap == covering) return;
        _coveringMinimap = covering;
        if (!hideMinimap) return; // this roof opts out — its presence doesn't fade the minimap
        bool wasCovered = _minimapHiderCount > 0;
        _minimapHiderCount += covering ? 1 : -1;
        if (_minimapHiderCount < 0) _minimapHiderCount = 0; // defensive
        bool isCovered = _minimapHiderCount > 0;
        if (wasCovered != isCovered) OnMinimapCoverageChanged?.Invoke(isCovered);
    }

    private void Refresh() {
        if (_hiders.Count > 0) Hide();
        else                   Show();
    }

    private void Show() {
        if (_ditherShader == null || _slots == null) { HardSetVisible(true); return; }
        SetPreventClick(true);
        if (_targetFade != 0f) { _targetFade = 0f; _fading = true; }
    }

    private void Hide() {
        if (_ditherShader == null || _slots == null) { HardSetVisible(false); return; }
        SetPreventClick(false);
        if (_targetFade != 1f) { _targetFade = 1f; _fading = true; }
    }

    // ── Fade driver ───────────────────────────────────────────────────────────

    private void Update() {
        if (!_fading) return;

        float speed = (_targetFade > _fade) ? hideFadeSpeed : showFadeSpeed;
        _fade = Mathf.MoveTowards(_fade, _targetFade, speed * Time.deltaTime);

        for (int i = 0; i < _slots.Length; i++) {
            FadeSlot s = _slots[i];
            if (s == null || s.renderer == null) continue;

            if (_fade > 0.0001f) {
                EnsureConverted(s);
                if (!s.renderer.enabled) s.renderer.enabled = true;
                s.renderer.GetPropertyBlock(_fadeBlock);
                _fadeBlock.SetFloat(FadeId, _fade);
                s.renderer.SetPropertyBlock(_fadeBlock);
            } else {
                // Fully visible again: clear the override and restore the original material.
                if (s.converted) {
                    s.renderer.GetPropertyBlock(_fadeBlock);
                    _fadeBlock.SetFloat(FadeId, 0f);
                    s.renderer.SetPropertyBlock(_fadeBlock);
                    RestoreOriginal(s);
                }
                if (!s.renderer.enabled) s.renderer.enabled = true;
            }
        }

        if (Mathf.Approximately(_fade, _targetFade)) {
            _fading = false;
            // Fully hidden: drop draw cost entirely (as the old hard toggle did). The
            // renderers re-enable themselves on the next show transition.
            if (_fade >= 1f) {
                for (int i = 0; i < _slots.Length; i++) {
                    if (_slots[i]?.renderer != null) _slots[i].renderer.enabled = false;
                }
            }
        }
    }

    // Instantly force the roof to a coherent visible/hidden state with no animation. Used on
    // disable, where Update won't run, so a re-enabled roof starts correct.
    private void SnapToState() {
        bool hidden = _hiders.Count > 0;
        _targetFade = hidden ? 1f : 0f;
        _fade       = _targetFade;
        _fading     = false;

        if (_slots != null) {
            for (int i = 0; i < _slots.Length; i++) {
                FadeSlot s = _slots[i];
                if (s == null || s.renderer == null) continue;
                if (hidden) {
                    s.renderer.enabled = false;
                } else {
                    if (s.converted) RestoreOriginal(s);
                    s.renderer.GetPropertyBlock(_fadeBlock);
                    _fadeBlock.SetFloat(FadeId, 0f);
                    s.renderer.SetPropertyBlock(_fadeBlock);
                    s.renderer.enabled = true;
                }
            }
        }
        SetPreventClick(!hidden);
    }

    private void HardSetVisible(bool visible) {
        for (int i = 0; i < this.renderersToHide.Count; i++) {
            if (this.renderersToHide[i] != null) this.renderersToHide[i].enabled = visible;
        }
        SetPreventClick(visible);
    }

    private void SetPreventClick(bool active) {
        if (this.preventClickChild != null && this.preventClickChild.activeSelf != active)
            this.preventClickChild.SetActive(active);
    }

    // ── Dither material swap (only while transitioning) ───────────────────────

    private void EnsureConverted(FadeSlot s) {
        if (s.converted || s.renderer == null || _ditherShader == null) return;
        Material[] src = s.renderer.sharedMaterials;
        if (s.original == null) s.original = src;
        if (s.dither == null) {
            s.dither = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) s.dither[i] = BuildDither(src[i]);
        }
        s.renderer.sharedMaterials = s.dither;
        s.converted = true;
    }

    private void RestoreOriginal(FadeSlot s) {
        if (!s.converted || s.renderer == null) return;
        if (s.original != null) s.renderer.sharedMaterials = s.original;
        s.converted = false;
    }

    // Builds a Sim/WallDither variant carrying the source material's maps/colour, so the
    // dissolving roof looks like itself while it fades. (Idle-visible roofs use the real
    // original material; this only shows during the transition.)
    private Material BuildDither(Material source) {
        if (source == null || _ditherShader == null) return source;
        Material m = new Material(_ditherShader) { name = source.name + " (" + _ditherShader.name + ")" };

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

        return m;
    }
}
