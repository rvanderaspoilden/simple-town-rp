using System.Linq;
using Interaction;
using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Client-side behaviour for Seat props (chairs, benches, couches, beds).
/// Handles any number of seat slots and couch slots on the same GameObject.
/// The server picks the first available slot; the client detects which slot
/// it was assigned by matching its netId in the incoming SeatState.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class SeatBehaviour : PropBehaviourBase, ISeatBehavior {
    [Header("Seat slots")]
    [SerializeField] private Transform[] seatTransforms;
    [SerializeField] private Transform[] couchTransforms;

    // Exposed so SeatPropSource can read slot counts without reflection
    public int SeatSlotCount  => seatTransforms?.Length ?? 0;
    public int CouchSlotCount => couchTransforms?.Length ?? 0;

    // Accès côté serveur (NPC IA) pour snapper sur le bon emplacement.
    public Transform[] SeatTransforms  => seatTransforms;
    public Transform[] CouchTransforms => couchTransforms;

    // ── Registre global propId → SeatBehaviour ──────────────────────────────────
    // Permet à SeatService (serveur) de retrouver l'instance d'un siège
    // sans FindObjectsByType. Indexé par propId (assigné via PropIdentity).
    private static readonly System.Collections.Generic.Dictionary<int, SeatBehaviour> _byPropId
        = new System.Collections.Generic.Dictionary<int, SeatBehaviour>();

    public static bool TryGet(int propId, out SeatBehaviour behaviour) =>
        _byPropId.TryGetValue(propId, out behaviour);

    public static System.Collections.Generic.IEnumerable<SeatBehaviour> All => _byPropId.Values;

    private int _registeredPropId = -1;

    /// <summary>
    /// Enregistre le siège dans le registre global. Idempotent et tolérant à un
    /// PropId pas encore assigné (cas runtime spawn : PropIdentity.Assign est
    /// appelé après Awake/OnEnable).
    /// </summary>
    private void RegisterIfReady() {
        var id = GetComponent<PropIdentity>();
        if (id == null || id.PropId <= 0) return;
        if (_registeredPropId == id.PropId) return;
        if (_registeredPropId > 0) _byPropId.Remove(_registeredPropId);
        _byPropId[id.PropId] = this;
        _registeredPropId = id.PropId;
    }

    private void OnEnable() {
        var id = GetComponent<PropIdentity>();
        if (id != null) id.OnAssigned += OnPropIdentityAssigned;
        RegisterIfReady();
    }

    private void OnDisable() {
        var id = GetComponent<PropIdentity>();
        if (id != null) id.OnAssigned -= OnPropIdentityAssigned;
        if (_registeredPropId > 0) _byPropId.Remove(_registeredPropId);
        _registeredPropId = -1;
    }

    private void OnPropIdentityAssigned(int id, string room) => RegisterIfReady();

    private SeatState _state;

    protected override void Awake() {
        base.Awake();
        _state = new SeatState {
            SeatOccupants  = new uint[SeatSlotCount],
            CouchOccupants = new uint[CouchSlotCount]
        };
    }

    // ── IPropBehaviour ────────────────────────────────────────────────────────

    public override void ApplyState(PropType type, byte[] payload) {
        base.ApplyState(type, payload);
        RegisterIfReady(); // PropIdentity peut avoir été assigné après Awake

        SeatState incoming = SeatState.Deserialize(payload);
        uint localNetId = PlayerController.Local?.netId ?? 0;

        if (localNetId != 0) {
            for (int i = 0; i < incoming.SeatOccupants.Length; i++) {
                bool wasHere = _state.SeatOccupants != null
                            && i < _state.SeatOccupants.Length
                            && _state.SeatOccupants[i] == localNetId;
                if (incoming.SeatOccupants[i] == localNetId && !wasHere && i < seatTransforms.Length) {
                    PlayerController.Local.Sit(this, seatTransforms[i]);
                }
            }
            for (int i = 0; i < incoming.CouchOccupants.Length; i++) {
                bool wasHere = _state.CouchOccupants != null
                            && i < _state.CouchOccupants.Length
                            && _state.CouchOccupants[i] == localNetId;
                if (incoming.CouchOccupants[i] == localNetId && !wasHere && i < couchTransforms.Length) {
                    PlayerController.Local.Sleep(this, couchTransforms[i]);
                }
            }
        }

        _state = incoming;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public override bool IsInteractable() {
        if (!base.IsInteractable()) return false;
        uint localNetId = PlayerController.Local?.netId ?? 0;
        if (IsLocalPlayerOccupying(localNetId)) return false;
        return HasAvailableSeat() || HasAvailableCouch();
    }

    public override Action[] GetActions(bool withPriority = false) {
        uint localNetId = PlayerController.Local?.netId ?? 0;
        if (IsLocalPlayerOccupying(localNetId)) return System.Array.Empty<Action>();

        return base.GetActions(withPriority).Where(a => {
            if (a.Type == ActionTypeEnum.SIT)   return HasAvailableSeat();
            if (a.Type == ActionTypeEnum.COUCH)  return HasAvailableCouch();
            return true;
        }).ToArray();
    }

    // ── ISeatBehavior ─────────────────────────────────────────────────────────

    public void RevokeSeat()  => SendPropInteraction(PropType.Seat, SeatInteraction.RevokeRequest);
    public void RevokeCouch() => SendPropInteraction(PropType.Seat, SeatInteraction.RevokeRequest);

    // ── PropBehaviourBase ─────────────────────────────────────────────────────

    protected override void Execute(Action action) {
        uint localNetId = PlayerController.Local?.netId ?? 0;
        if (IsLocalPlayerOccupying(localNetId)) return;

        switch (action.Type) {
            case ActionTypeEnum.SIT when HasAvailableSeat():
                SendPropInteraction(PropType.Seat, SeatInteraction.SitRequest);
                break;
            case ActionTypeEnum.COUCH when HasAvailableCouch():
                SendPropInteraction(PropType.Seat, SeatInteraction.CouchRequest);
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool HasAvailableSeat()  => _state.SeatOccupants  != null && System.Array.Exists(_state.SeatOccupants,  id => id == 0);
    private bool HasAvailableCouch() => _state.CouchOccupants != null && System.Array.Exists(_state.CouchOccupants, id => id == 0);

    private bool IsLocalPlayerOccupying(uint netId) {
        if (netId == 0) return false;
        return (_state.SeatOccupants  != null && System.Array.Exists(_state.SeatOccupants,  id => id == netId))
            || (_state.CouchOccupants != null && System.Array.Exists(_state.CouchOccupants, id => id == netId));
    }
}
