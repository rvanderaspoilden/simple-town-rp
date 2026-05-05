using System.Collections.Generic;
using Mirror;
using Sim;
using Sim.Building;
using UnityEngine;

/// <summary>
/// Single NetworkBehaviour managing state for all stateless-by-design city props (doors, seats).
/// Replaces per-prop NetworkIdentities with one shared authority object in the City scene.
/// City props register themselves at Start() and receive state changes via SyncDictionary callbacks.
/// </summary>
public class CityPropsStateManager : NetworkBehaviour {
    public static CityPropsStateManager Instance { get; private set; }

    // propId → isOpen
    private readonly SyncDictionary<int, bool> doorOpenStates = new SyncDictionary<int, bool>();

    // key = propId * 100 + seatIdx → playerNetId (uint)
    private readonly SyncDictionary<int, uint> seatOccupants = new SyncDictionary<int, uint>();

    // key = propId * 100 + couchIdx → playerNetId (uint)
    private readonly SyncDictionary<int, uint> couchOccupants = new SyncDictionary<int, uint>();

    private readonly Dictionary<int, CityDoor> doors = new Dictionary<int, CityDoor>();
    private readonly Dictionary<int, CitySeat> seats = new Dictionary<int, CitySeat>();

    private void Awake() {
        Instance = this;
    }

    public override void OnStartServer() {
        base.OnStartServer();
        SimpleTownNetwork.OnPlayerDisconnected += ServerOnPlayerDisconnected;
    }

    public override void OnStopServer() {
        base.OnStopServer();
        SimpleTownNetwork.OnPlayerDisconnected -= ServerOnPlayerDisconnected;
    }

    public override void OnStartClient() {
        base.OnStartClient();
        doorOpenStates.OnChange += OnDoorOpenStateChanged;
    }

    public override void OnStopClient() {
        base.OnStopClient();
        doorOpenStates.OnChange -= OnDoorOpenStateChanged;
    }

    // --- Registration (called from city prop Start()) ---

    public void RegisterDoor(int propId, CityDoor door) {
        doors[propId] = door;

        if (isServer && !doorOpenStates.ContainsKey(propId)) {
            doorOpenStates[propId] = false;
        }

        // Apply existing state for late-joining clients (OnDeserializeAll doesn't trigger callbacks)
        if (isClient && doorOpenStates.TryGetValue(propId, out bool isOpen)) {
            door.ApplyOpenState(isOpen);
        }
    }

    public void RegisterSeat(int propId, CitySeat seat) {
        seats[propId] = seat;
    }

    // --- Server API for door trigger events ---

    [Server]
    public void ServerDoorTriggersChanged(int propId, bool hasOccupants, DoorLockState lockState) {
        bool shouldBeOpen = hasOccupants && lockState == DoorLockState.UNLOCKED;
        if (doorOpenStates.TryGetValue(propId, out bool current) && current == shouldBeOpen) return;
        doorOpenStates[propId] = shouldBeOpen;
    }

    // --- Client → server commands ---

    [Command(requiresAuthority = false)]
    public void CmdRequestSit(int propId, int seatIdx, NetworkConnectionToClient sender = null) {
        if (sender == null) return;
        int key = propId * 100 + seatIdx;
        if (seatOccupants.ContainsKey(key)) return;
        seatOccupants[key] = sender.identity.netId;
        TargetConfirmSit(sender, propId, seatIdx);
    }

    [Command(requiresAuthority = false)]
    public void CmdRevokeSeat(int propId, NetworkConnectionToClient sender = null) {
        if (sender == null) return;
        uint netId = sender.identity.netId;
        int keyToRemove = FindOccupantKey(seatOccupants, propId, netId);
        if (keyToRemove != -1) seatOccupants.Remove(keyToRemove);
    }

    [Command(requiresAuthority = false)]
    public void CmdRequestCouch(int propId, int couchIdx, NetworkConnectionToClient sender = null) {
        if (sender == null) return;
        int key = propId * 100 + couchIdx;
        if (couchOccupants.ContainsKey(key)) return;
        couchOccupants[key] = sender.identity.netId;
        if (NetworkServer.spawned.TryGetValue(sender.identity.netId, out NetworkIdentity ni)) {
            ni.GetComponent<PlayerController>().PlayerState = PlayerState.SLEEPING;
        }
        TargetConfirmCouch(sender, propId, couchIdx);
    }

    [Command(requiresAuthority = false)]
    public void CmdRevokeCouch(int propId, NetworkConnectionToClient sender = null) {
        if (sender == null) return;
        uint netId = sender.identity.netId;
        int keyToRemove = FindOccupantKey(couchOccupants, propId, netId);
        if (keyToRemove == -1) return;
        couchOccupants.Remove(keyToRemove);
        if (NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity ni)) {
            ni.GetComponent<PlayerController>().PlayerState = PlayerState.IDLE;
        }
    }

    // --- Target RPCs (server → specific client) ---

    [TargetRpc]
    private void TargetConfirmSit(NetworkConnection target, int propId, int seatIdx) {
        if (seats.TryGetValue(propId, out CitySeat seat)) {
            PlayerController.Local.Sit(seat, seat.GetSeatTransform(seatIdx));
        }
    }

    [TargetRpc]
    private void TargetConfirmCouch(NetworkConnection target, int propId, int couchIdx) {
        if (seats.TryGetValue(propId, out CitySeat seat)) {
            PlayerController.Local.Sleep(seat, seat.GetCouchTransform(couchIdx));
        }
    }

    // --- Disconnect cleanup ---

    [Server]
    private void ServerOnPlayerDisconnected(NetworkConnectionToClient conn) {
        if (conn.identity == null) return;
        uint playerNetId = conn.identity.netId;
        RemovePlayerFromDict(seatOccupants, playerNetId);
        RemovePlayerFromDict(couchOccupants, playerNetId);
    }

    // --- SyncDict change callback ---

    private void OnDoorOpenStateChanged(SyncIDictionary<int, bool>.Operation op, int key, bool value) {
        // For OP_ADD: value = new value. For OP_SET: value = old value → read from dict.
        bool newState = op == SyncIDictionary<int, bool>.Operation.OP_ADD
            ? value
            : doorOpenStates.TryGetValue(key, out bool v) ? v : value;

        if (doors.TryGetValue(key, out CityDoor door)) {
            door.ApplyOpenState(newState);
        }
    }

    // --- Public query API for CitySeat.GetActions() ---

    public bool IsSeatOccupied(int propId, int seatIdx) => seatOccupants.ContainsKey(propId * 100 + seatIdx);

    public bool IsCouchOccupied(int propId, int couchIdx) => couchOccupants.ContainsKey(propId * 100 + couchIdx);

    public bool IsPlayerInSeat(int propId, uint playerNetId) => FindOccupantKey(seatOccupants, propId, playerNetId) != -1;

    public bool IsPlayerOnCouch(int propId, uint playerNetId) => FindOccupantKey(couchOccupants, propId, playerNetId) != -1;

    // --- Helpers ---

    private static int FindOccupantKey(SyncDictionary<int, uint> dict, int propId, uint playerNetId) {
        foreach (KeyValuePair<int, uint> kv in dict) {
            if (kv.Key / 100 == propId && kv.Value == playerNetId) return kv.Key;
        }
        return -1;
    }

    private static void RemovePlayerFromDict(SyncDictionary<int, uint> dict, uint playerNetId) {
        List<int> toRemove = new List<int>();
        foreach (KeyValuePair<int, uint> kv in dict) {
            if (kv.Value == playerNetId) toRemove.Add(kv.Key);
        }
        foreach (int key in toRemove) dict.Remove(key);
    }
}
