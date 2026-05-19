using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// UI/interaction component for elevator props. Server-side routing is handled
/// directly by PropInteractionDispatcher using the player's roomId — this
/// behaviour holds no server state of its own.
///
/// The <see cref="HallController"/> property is set by HallController on its
/// own elevator instance and read client-side by ElevatorUI to compute the
/// origin floor before sending C2S_TeleporterUse (lets the client short-circuit
/// no-op clicks).
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class TeleporterBehaviour : PropBehaviourBase {
    [Header("Teleporter")]
    [SerializeField] private Transform spawnTransform;

    private HallController _hallController;

    public Transform SpawnTransform => spawnTransform;

    public HallController HallController {
        get => _hallController;
        set => _hallController = value;
    }

    // ── IInteractable overrides ───────────────────────────────────────────────

    public override void StopInteraction() {
        DefaultViewUI.Instance?.HideElevatorUI();
    }

    // ── PropBehaviourBase hook ────────────────────────────────────────────────

    protected override void Execute(Action action) {
        if (action.Type == ActionTypeEnum.TELEPORT) {
            PlayerController.Local?.Interact(this);
            DefaultViewUI.Instance?.ShowElevatorUI(this);
        }
    }
}
