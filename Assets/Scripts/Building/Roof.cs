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

    private readonly HashSet<object> _hiders = new HashSet<object>();

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
        this.renderersToHide.ForEach(x => x.material = new Material(x.material));
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
        Refresh();
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
        this.renderersToHide.ForEach(x => x.enabled = true);
        if (this.preventClickChild != null) this.preventClickChild.SetActive(true);
    }

    private void Hide() {
        this.renderersToHide.ForEach(x => x.enabled = false);
        if (this.preventClickChild != null) this.preventClickChild.SetActive(false);
    }
}
