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

    private readonly HashSet<object> _hiders = new HashSet<object>();

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
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            AddHider(PlayerKey);
        } else if (other.CompareTag("MainCamera")) {
            AddHider(CameraKey);
        }
    }

    private void OnTriggerExit(Collider other) {
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            RemoveHider(PlayerKey);
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
