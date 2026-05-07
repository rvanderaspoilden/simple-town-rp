using System.Collections.Generic;
using Mirror;
using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Replaces the legacy Teleporter NetworkBehaviour.
/// Extends PropBehaviourBase — registered in the prop system for city scene props.
/// Also maintains a server-side static registry (by roomId) so that C2S_TeleporterUse
/// can be routed to the right elevator without needing a PropId in the message.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class TeleporterBehaviour : PropBehaviourBase {
    [Header("Teleporter")]
    [SerializeField] private Transform spawnTransform;

    private HallController _hallController;
    private string _serverRoomId;

    // Server-side only: roomId → TeleporterBehaviour
    private static readonly Dictionary<string, TeleporterBehaviour> _registry
        = new Dictionary<string, TeleporterBehaviour>();

    public delegate void UseEvent(int originFloor, int floorDestination, NetworkConnectionToClient playerConn);
    public event UseEvent OnUse;

    public Transform SpawnTransform => spawnTransform;

    public HallController HallController {
        get => _hallController;
        set => _hallController = value;
    }

    protected override void OnDestroy() {
        base.OnDestroy();
        if (!string.IsNullOrEmpty(_serverRoomId))
            _registry.Remove(_serverRoomId);
    }

    // ── Server-side setup ─────────────────────────────────────────────────────

    /// <summary>
    /// Call on the server once the room context is known.
    /// Registers this elevator in the routing registry so C2S_TeleporterUse can find it.
    /// </summary>
    public void InitServerSide(string roomId) {
        _serverRoomId = roomId;
        _registry[roomId] = this;
    }

    public static bool TryGetByRoom(string roomId, out TeleporterBehaviour teleporter) =>
        _registry.TryGetValue(roomId, out teleporter);

    /// <summary>
    /// Called server-side when a C2S_TeleporterUse arrives from a player in this elevator's room.
    /// </summary>
    public void ServerHandleUse(int floorDestination, NetworkConnectionToClient conn) {
        if (_hallController == null)
            _hallController = GetComponentInParent<HallController>();

        int originFloor = _hallController ? _hallController.FloorNumber : 0;

        if (originFloor != floorDestination)
            OnUse?.Invoke(originFloor, floorDestination, conn);
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
