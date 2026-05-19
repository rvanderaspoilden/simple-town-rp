using Sim;
using UnityEngine;

/// <summary>
/// Generic trigger volume that hides a target <see cref="Roof"/> while the local
/// player or the main camera is inside, as long as the gate is open.
///
/// Typical use: attach to a child of a door's GameObject with a BoxCollider (trigger)
/// spanning the doorway from outside, and let <c>DoorBehaviour</c> flip <see cref="GateOpen"/>
/// when the door swings open. The system is intentionally agnostic of what kind of opening
/// it sits on — windows, removed walls or balconies plug in the same way.
///
/// Resolution of the target roof:
///   - If <c>targetRoof</c> is wired in the inspector, it's used as-is.
///   - Otherwise we look up the nearest registered <see cref="Roof"/> whose XZ footprint
///     contains (or is closest to) this trigger's center. Client-side props are NOT
///     parented under their apartment, so a hierarchy walk wouldn't work — the spatial
///     match is the only robust option.
///   - Resolution is lazy: it happens on the first occupant event, then is cached.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RoofRevealTrigger : MonoBehaviour {
    [Header("Target")]
    [Tooltip("Roof to hide while occupied. If null, the component spatially resolves the nearest registered Roof on its first occupant event.")]
    [SerializeField] private Roof targetRoof;

    [Header("Gate")]
    [Tooltip("When false, the trigger never contributes to the target roof, even with someone inside. " +
             "DoorBehaviour ties this to the door's open state.")]
    [SerializeField] private bool gateOpen = true;

    private bool playerInside;
    private bool cameraInside;
    private bool contributing; // currently in targetRoof._hiders

    public Roof TargetRoof {
        get => targetRoof;
        set {
            if (targetRoof == value) return;
            ReleaseIfContributing();
            targetRoof = value;
            Sync();
        }
    }

    public bool GateOpen {
        get => gateOpen;
        set {
            if (gateOpen == value) return;
            gateOpen = value;
            Sync();
        }
    }

    private void OnDisable() {
        ReleaseIfContributing();
        playerInside = false;
        cameraInside = false;
    }

    private void OnTriggerStay(Collider other) {
        bool changed = false;
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            if (!playerInside) { playerInside = true; changed = true; }
        } else if (other.CompareTag("MainCamera")) {
            if (!cameraInside) { cameraInside = true; changed = true; }
        }
        if (changed) Sync();
    }

    private void OnTriggerExit(Collider other) {
        bool changed = false;
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            if (playerInside) { playerInside = false; changed = true; }
        } else if (other.CompareTag("MainCamera")) {
            if (cameraInside) { cameraInside = false; changed = true; }
        }
        if (changed) Sync();
    }

    private void Sync() {
        if (targetRoof == null) targetRoof = ResolveRoof();

        bool shouldContribute = gateOpen && targetRoof != null && (playerInside || cameraInside);
        if (shouldContribute && !contributing) {
            targetRoof.AddHider(this);
            contributing = true;
        } else if (!shouldContribute && contributing) {
            targetRoof.RemoveHider(this);
            contributing = false;
        }
    }

    private void ReleaseIfContributing() {
        if (contributing && targetRoof != null) targetRoof.RemoveHider(this);
        contributing = false;
    }

    /// <summary>
    /// Picks the registered Roof whose XZ footprint best matches this trigger's
    /// world position. Containment wins (distance 0); otherwise the smallest
    /// XZ distance to a footprint AABB wins. Returns null if no roofs are registered.
    /// </summary>
    private Roof ResolveRoof() {
        if (Roof.All.Count == 0) return null;

        Vector3 p = transform.position;
        Roof best = null;
        float bestDist = float.PositiveInfinity;

        foreach (Roof r in Roof.All) {
            if (r == null) continue;
            Bounds b = r.GetXZFootprint();

            float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);
            float dist = dx * dx + dz * dz; // squared, no sqrt needed

            if (dist < bestDist) {
                bestDist = dist;
                best = r;
                if (dist <= 0f) break; // exact containment; nothing closer possible
            }
        }
        return best;
    }
}
