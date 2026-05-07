using System;
using System.Collections.Generic;
using Mirror;
using Sim.Entities;
using UnityEngine;

public class BuildingBehavior : NetworkBehaviour {
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
    }

    public static bool TryGetBuilding(string street, out BuildingBehavior building) =>
        _buildingRegistry.TryGetValue(street, out building);

    // ── Client-side hall registry (used by HallController) ───────────────────

    public static void RegisterClientHall(string street, int floor, HallController hall) {
        _clientHalls[(street, floor)] = hall;
    }

    public static void UnregisterClientHall(string street, int floor) {
        _clientHalls.Remove((street, floor));
    }

    // ── Mirror server lifecycle ───────────────────────────────────────────────

    public override void OnStartServer() {
        this.mainElevator.InitServerSide("city");
        this.mainElevator.OnUse += TeleportToFloor;
    }

    public override void OnStopServer() {
        this.mainElevator.OnUse -= TeleportToFloor;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int GetFloorByDoorNumber(int doorNumber) =>
        Mathf.CeilToInt(doorNumber / (float)this.nbDoorByFloor);

    public bool Match(Address address) => this.streetName == address.street;

    public bool MatchStreet(string street) => this.streetName == street;

    // ── Server: teleport player to their apartment ────────────────────────────

    [Server]
    public void TeleportToApartment(int doorNumber, NetworkConnection conn) {
        Debug.Log($"Server: Teleport player to apartment {doorNumber}");

        int targetFloor = this.GetFloorByDoorNumber(doorNumber);

        if (!hallControllerByFloor.ContainsKey(targetFloor)) {
            CreateHall(targetFloor);
        }

        HallController hallController = hallControllerByFloor[targetFloor];
        hallController.CheckApartmentState(doorNumber);
        hallController.MoveToApartment(doorNumber, conn);
    }

    [ServerCallback]
    public void TeleportToFloor(int originFloor, int targetFloor, NetworkConnectionToClient conn) {
        Debug.Log($"Server: Teleport from {originFloor} to {targetFloor}");

        if (targetFloor == 0) {
            conn.Send(new TeleportMessage {
                destination = this.mainElevator.SpawnTransform.position,
                NewRoomId   = "city"
            });
        } else {
            if (!hallControllerByFloor.ContainsKey(targetFloor)) {
                CreateHall(targetFloor);
            }
            hallControllerByFloor[targetFloor].MoveToSpawn(conn);
        }

        if (originFloor > 0 && hallControllerByFloor.ContainsKey(originFloor)) {
            hallControllerByFloor[originFloor].RemovePlayer(conn.identity);
            this.TryToCleanHall(hallControllerByFloor[originFloor]);
        }
    }

    [Server]
    private void CreateHall(int targetFloor) {
        Vector3 spawnPos = this.GetSpawnPositionForHall(targetFloor);
        HallController newHall = Instantiate(this.hallPrefab, spawnPos, Quaternion.identity);

        Debug.Log($"[BuildingBehavior] Hall created at {spawnPos.y} for floor {targetFloor}");

        newHall.Init(streetName, targetFloor, this);
        newHall.Elevator.OnUse += TeleportToFloor;

        hallControllerByFloor.Add(targetFloor, newHall);

        // Inform all clients so they can instantiate their local copy
        NetworkServer.SendToAll(new S2C_HallSpawn {
            Street      = streetName,
            FloorNumber = targetFloor,
            Position    = spawnPos
        });
    }

    [Server]
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
        if (_clientHalls.ContainsKey((msg.Street, msg.FloorNumber))) return; // already exists

        if (NetworkServer.active) {
            // Host mode: server already created the hall; ClientSetup will be called
            // when S2C_ApartmentSpawn arrives (ClientSpawnApartment sets it up)
            if (hallControllerByFloor.TryGetValue(msg.FloorNumber, out HallController existing)) {
                existing.ClientSetup(msg.Street, msg.FloorNumber);
            }
            return;
        }

        // Client-only mode: instantiate hall prefab locally
        HallController hall = Instantiate(hallPrefab, msg.Position, Quaternion.identity);
        hall.ClientSetup(msg.Street, msg.FloorNumber);
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

    // ── Utility ───────────────────────────────────────────────────────────────

    private Vector3 GetSpawnPositionForHall(int floorNumber) {
        int x = floorNumber - (Mathf.FloorToInt(floorNumber / 10f) * 10);
        return new Vector3(x * 100, -100 + (-100 * floorNumber % 10), 0);
    }
}
