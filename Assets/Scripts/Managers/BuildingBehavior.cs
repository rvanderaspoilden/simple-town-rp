using System;
using System.Collections.Generic;
using Mirror;
using Sim.Entities;
using UnityEngine;

// Plain MonoBehaviour. The building has no per-field network state (no SyncVar,
// no ClientRpc/TargetRpc) — all gameplay sync goes through custom NetworkMessages
// (S2C_HallSpawn, S2C_HallDespawn, S2C_ApartmentSpawn, TeleportMessage).
// Lifecycle is driven explicitly by SimpleTownNetwork via ServerInit/ServerShutdown.
public class BuildingBehavior : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private string streetName;

    [SerializeField]
    private HallController hallPrefab;

    [SerializeField]
    private int nbDoorByFloor;

    [SerializeField]
    private TeleporterBehaviour mainElevator;

    // Server-side: floor → HallController
    private readonly Dictionary<int, HallController> hallControllerByFloor = new Dictionary<int, HallController>();

    // Client-side: (street, floor) → HallController  (used by S2C handlers)
    private static readonly Dictionary<(string, int), HallController> _clientHalls
        = new Dictionary<(string, int), HallController>();

    // Per-street building registry so S2C handlers can route to the right building
    private static readonly Dictionary<string, BuildingBehavior> _buildingRegistry
        = new Dictionary<string, BuildingBehavior>();

    private void Awake() {
        _buildingRegistry[streetName] = this;
    }

    private void OnDestroy() {
        _buildingRegistry.Remove(streetName);
        // Allow the inclusive scan to re-run on the next lookup (e.g. after a
        // scene reload). Awake won't re-fire on the new BuildingBehavior if it's
        // still on an inactive GameObject.
        _inactiveScanDone = false;
    }

    public string StreetName => streetName;

    /// <summary>
    /// Lookup a BuildingBehavior by street name.
    ///
    /// Robust against scenes where the BuildingBehavior is on an inactive GameObject:
    /// in that case <c>Awake()</c> never runs and the registry stays empty, so we
    /// fall back to a one-time inclusive scan (FindObjectsInactive.Include) and
    /// populate the registry on demand. Subsequent lookups hit the registry directly.
    /// </summary>
    public static bool TryGetBuilding(string street, out BuildingBehavior building) {
        if (_buildingRegistry.TryGetValue(street, out building)) return true;

        // Fallback: discover inactive instances and register them.
        DiscoverInactiveBuildings();
        return _buildingRegistry.TryGetValue(street, out building);
    }

    private static bool _inactiveScanDone;
    public static void DiscoverInactiveBuildings() {
        if (_inactiveScanDone) return;
        _inactiveScanDone = true;

        BuildingBehavior[] all = FindObjectsByType<BuildingBehavior>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (BuildingBehavior bb in all) {
            if (string.IsNullOrEmpty(bb.streetName)) continue;
            if (!_buildingRegistry.ContainsKey(bb.streetName)) {
                _buildingRegistry[bb.streetName] = bb;
                Debug.Log($"[Building] Discovered inactive BuildingBehavior street={bb.streetName} — registered for client routing");
            }
        }
    }

    // ── Client-side hall registry (used by HallController) ───────────────────

    public static void RegisterClientHall(string street, int floor, HallController hall) {
        _clientHalls[(street, floor)] = hall;
    }

    public static void UnregisterClientHall(string street, int floor) {
        _clientHalls.Remove((street, floor));
    }

    // ── Server lifecycle (driven by SimpleTownNetwork.OnStartServer/OnStopServer) ─

    public void ServerInit() {
        if (this.mainElevator == null) {
            Debug.LogError($"[Building] ServerInit street={streetName}: mainElevator is null");
            return;
        }
        this.mainElevator.InitServerSide("city");
        this.mainElevator.OnUse += TeleportToFloor;
    }

    public void ServerShutdown() {
        if (this.mainElevator != null) {
            this.mainElevator.OnUse -= TeleportToFloor;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int GetFloorByDoorNumber(int doorNumber) =>
        Mathf.CeilToInt(doorNumber / (float)this.nbDoorByFloor);

    public bool Match(Address address) => this.streetName == address.street;

    public bool MatchStreet(string street) => this.streetName == street;

    // ── Server: teleport player to their apartment ────────────────────────────

    /// <summary>
    /// Entry point for a NEW player login. The playerGo is not yet network-spawned;
    /// HallController.CheckGenerationState will call FinalizePlayerSpawn once the floor
    /// is fully loaded. This ensures the lifecycle order:
    ///   LoadFloorData → BuildHall → RegisterEntities → AddPlayerForConnection → TeleportMessage
    /// </summary>
    public void TeleportToApartment(int doorNumber, NetworkConnectionToClient conn, GameObject playerGo) {
        Debug.Log($"[Building] TeleportToApartment building={streetName} door={doorNumber} conn={conn.connectionId}");

        int targetFloor = this.GetFloorByDoorNumber(doorNumber);

        if (!hallControllerByFloor.ContainsKey(targetFloor)) {
            CreateHall(targetFloor);
        } else {
            // Hall already cached for an earlier player — replay the spawn
            // snapshot to this connection so it gets its local HallController.
            hallControllerByFloor[targetFloor].SendSnapshotTo(conn);
        }

        HallController hallController = hallControllerByFloor[targetFloor];
        hallController.CheckApartmentState(doorNumber);
        hallController.MoveToApartment(doorNumber, conn, playerGo);
    }

    /// <summary>
    /// Teleports a player that is already network-spawned back to their apartment.
    /// Used by PlayerController.Revive() — no playerGo needed.
    /// </summary>
    public void TeleportExistingPlayerToApartment(int doorNumber, NetworkConnectionToClient conn) {
        Debug.Log($"[Building] TeleportExistingPlayer building={streetName} door={doorNumber} conn={conn.connectionId}");

        int targetFloor = this.GetFloorByDoorNumber(doorNumber);

        if (!hallControllerByFloor.ContainsKey(targetFloor)) {
            CreateHall(targetFloor);
        } else {
            hallControllerByFloor[targetFloor].SendSnapshotTo(conn);
        }

        HallController hallController = hallControllerByFloor[targetFloor];
        hallController.CheckApartmentState(doorNumber);
        // Pass null as playerGo — player is already spawned, FinalizeAndTeleport skips AddPlayerForConnection.
        hallController.MoveToApartment(doorNumber, conn, null);
    }

    public void TeleportToFloor(int originFloor, int targetFloor, NetworkConnectionToClient conn) {
        if (!NetworkServer.active) return;
        Debug.Log($"[Building] TeleportToFloor building={streetName} from={originFloor} to={targetFloor} conn={conn.connectionId}");

        if (targetFloor == 0) {
            if (this.mainElevator == null || this.mainElevator.SpawnTransform == null) {
                Debug.LogError($"[Building] TeleportToFloor to city failed — mainElevator or its SpawnTransform is null on building {streetName}");
                return;
            }
            conn.Send(new TeleportMessage {
                destination = this.mainElevator.SpawnTransform.position,
                NewRoomId   = "city"
            });
        } else {
            bool createdNewHall = !hallControllerByFloor.ContainsKey(targetFloor);
            if (createdNewHall) {
                Debug.Log($"[Building] TeleportToFloor: creating new hall floor={targetFloor} for conn={conn.connectionId}");
                CreateHall(targetFloor);
            } else {
                Debug.Log($"[Building] TeleportToFloor: reusing existing hall floor={targetFloor}");
                hallControllerByFloor[targetFloor].SendSnapshotTo(conn);
            }
            // Existing player using elevator — no playerGo needed (already spawned).
            hallControllerByFloor[targetFloor].MoveToSpawn(conn);
        }

        if (originFloor > 0 && hallControllerByFloor.ContainsKey(originFloor)) {
            hallControllerByFloor[originFloor].RemovePlayer(conn.identity);
            this.TryToCleanHall(hallControllerByFloor[originFloor]);
        }
    }

    private void CreateHall(int targetFloor) {
        Vector3 spawnPos = this.GetSpawnPositionForHall(targetFloor);
        HallController newHall = Instantiate(this.hallPrefab, spawnPos, Quaternion.identity);

        Debug.Log($"[Hall] Building runtime hall for room hall:{streetName}:{targetFloor} at {spawnPos}");

        newHall.Init(streetName, targetFloor, this);
        newHall.Elevator.OnUse += TeleportToFloor;

        hallControllerByFloor.Add(targetFloor, newHall);

        // Inform all clients so they can instantiate their local copy.
        NetworkServer.SendToAll(new S2C_HallSpawn {
            Street      = streetName,
            FloorNumber = targetFloor,
            Position    = spawnPos
        });
    }

    public void TryToCleanHall(HallController hallController) {
        if (!hallController.ContainPlayers()) {
            hallController.Elevator.OnUse -= TeleportToFloor;
            hallControllerByFloor.Remove(hallController.FloorNumber);

            NetworkServer.SendToAll(new S2C_HallDespawn {
                Street      = streetName,
                FloorNumber = hallController.FloorNumber
            });

            hallController.ClientDespawn(); // clean client dict before Destroy
            Destroy(hallController.gameObject);
        } else {
            Debug.Log($"Can't destroy hall with floor number {hallController.FloorNumber} because not empty");
        }
    }

    // ── Client-side S2C handlers (called from SimpleTownNetwork) ─────────────

    public void OnClientHallSpawn(S2C_HallSpawn msg) {
        if (_clientHalls.ContainsKey((msg.Street, msg.FloorNumber))) {
            Debug.Log($"[Hall] OnClientHallSpawn skipped — already exists street={msg.Street} floor={msg.FloorNumber}");
            return;
        }

        if (NetworkServer.active) {
            // Host mode: server already created the hall; just register it client-side.
            if (hallControllerByFloor.TryGetValue(msg.FloorNumber, out HallController existing)) {
                Debug.Log($"[Hall] Host-mode setup of existing hall street={msg.Street} floor={msg.FloorNumber}");
                existing.ClientSetup(msg.Street, msg.FloorNumber);
            } else {
                Debug.LogError($"[Hall] Host-mode: server hall not found for floor={msg.FloorNumber}");
            }
            return;
        }

        if (hallPrefab == null) {
            Debug.LogError($"[Hall] hallPrefab is NULL on BuildingBehavior street={streetName} — check scene/prefab assignment");
            return;
        }

        Debug.Log($"[Hall] Instantiating hall prefab prefabId={hallPrefab.name} street={msg.Street} floor={msg.FloorNumber} pos={msg.Position}");
        HallController hall = Instantiate(hallPrefab, msg.Position, Quaternion.identity);
        hall.ClientSetup(msg.Street, msg.FloorNumber);
        Debug.Log($"[Hall] Runtime HallController created room=hall:{msg.Street}:{msg.FloorNumber}");
    }

    public void OnClientHallDespawn(S2C_HallDespawn msg) {
        if (!_clientHalls.TryGetValue((msg.Street, msg.FloorNumber), out HallController hall)) return;

        if (!NetworkServer.active) {
            // Client-only: hall was instantiated locally; destroy it
            hall.ClientDespawn();
            Destroy(hall.gameObject);
        }
        _clientHalls.Remove((msg.Street, msg.FloorNumber));
    }

    public void OnClientApartmentSpawn(S2C_ApartmentSpawn msg) {
        if (!_clientHalls.TryGetValue((msg.Street, msg.FloorNumber), out HallController hall)) {
            Debug.LogWarning($"[BuildingBehavior] S2C_ApartmentSpawn: hall ({msg.Street},{msg.FloorNumber}) not found — ensure S2C_HallSpawn is processed first");
            return;
        }
        hall.ClientSpawnApartment(msg);
    }

    /// <summary>
    /// Destroys all client-side halls whose roomId does not match <paramref name="keepRoomId"/>.
    /// Called on teleport so stale ApartmentController GOs are cleaned up when
    /// the player leaves a building.
    /// </summary>
    public static void ClientDespawnHallsExcept(string keepRoomId) {
        var toRemove = new System.Collections.Generic.List<(string, int)>();
        foreach (var kv in _clientHalls) {
            if (kv.Value != null && kv.Value.RoomId != keepRoomId)
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove) {
            if (!_clientHalls.TryGetValue(key, out var hall) || hall == null) continue;
            if (!NetworkServer.active) {
                hall.ClientDespawn();
                UnityEngine.Object.Destroy(hall.gameObject);
            }
            _clientHalls.Remove(key);
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    // Layout: 9 columns × N rows, each cell 100 units apart.
    // floor 1-9  → row 0, y = -100
    // floor 10-18 → row 1, y = -200
    // No two floors share the same (x,y) position.
    private Vector3 GetSpawnPositionForHall(int floorNumber) {
        int col = (floorNumber - 1) % 9;        // 0..8
        int row = (floorNumber - 1) / 9;        // 0, 1, 2 …
        return new Vector3((col + 1) * 100f, -100f - row * 100f, 0f);
    }
}
