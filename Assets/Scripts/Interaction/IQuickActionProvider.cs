using Sim.Interactables;

namespace Interaction {
    /// <summary>
    /// Implemented by interactables that expose a single "quick" (priority) action,
    /// triggered by a short E tap in TPS (a long press opens the full radial menu).
    /// The action is resolved by type against the interactable's currently available
    /// actions, so permission/state gating stays authoritative.
    /// </summary>
    public interface IQuickActionProvider {
        /// <summary>
        /// Resolves the quick action among <paramref name="available"/> (typically the
        /// priority-filtered action set). Returns false when no quick action is
        /// configured or none matches — the caller then falls back to the radial menu.
        /// </summary>
        bool TryGetQuickAction(Action[] available, out Action action);
    }
}
