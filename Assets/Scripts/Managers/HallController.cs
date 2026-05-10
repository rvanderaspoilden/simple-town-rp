using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Entities;
using Sim.Enums;
using UnityEngine;

public class HallController : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private ApartmentController talyahPrefab;

    [SerializeField]
    private Transform[] apartmentSpawnPoints;

    [SerializeField]
    private TeleporterBehaviour elevatorPrefab;

    [SerializeField]
    private Transform elevatorSpawn;

    [SerializeField]
    private GeographicArea geographicArea;

    private readonly HashSet<NetworkConnectionToClient> playersInside = new HashSet<NetworkConnectionToClient>();

    private string street;
    private int floorNumber;
    private bool isGenerated;
    // Guard: prevent CheckGenerationState from broadcasting more than once.
    // Reset when an apartment calls Regenerate so that a re-gen cycle re-broadcasts.
    private bool _generationBroadcasted;

    private TeleporterBehaviour elevator;
    private BuildingBehavior associatedBuilding;

    private readonly HashSet<ApartmentController> generatedApartments = new HashSet<ApartmentController>();

    // Key: connection waiting for this floor to be ready.
    // Value: (playerGo, doorNumber).  doorNumber == -1 means "teleport to elevator spawn".
    // playerGo is null for existing (already-spawned) players using the elevator.
    private readonly Dictionary<NetworkConnectionToClient, (GameObject playerGo, int doorNumber)> playersToMove
        = new Dictionary<NetworkConnectionToClient, (GameObject, int)>();

    // Client-side apartment registry (doorNumber → controller)
    private readonly Dictionary<int, ApartmentController> _clientApartments = new Dictionary<int, ApartmentController>();

    // ── Server-side init ──────────────────────────────────────────────────────

    [Server]
    public void Init(string streetName, int floor, BuildingBehavior building) {
        this.floorNumber        = floor;
        this.street             = streetName;
        this.associatedBuilding = building;

        SimpleTownNetwork.OnPlayerDisconnected += RemoveDisconnectedPlayer;

        this.elevator = Instantiate(this.elevatorPrefab, this.elevatorSpawn.position, this.elevatorSpawn.rotation);
        this.elevator.transform.parent   = this.transform;
        this.elevator.HallController     = this;
        this.elevator.InitServerSide(RoomId);

        for (int i = 0; i < this.apartmentSpawnPoints.Length; i++) {
            Address address = new Address {
                street     = this.street,
                doorNumber = (i + 1) + (this.apartmentSpawnPoints.Length * (this.floorNumber - 1)),
                homeType   = HomeTypeEnum.APARTMENT
            };

            ApartmentController newApartment = Instantiate(
                this.talyahPrefab,
                this.apartmentSpawnPoints[i].position,
                this.apartmentSpawnPoints[i].rotation
            );

            newApartment.transform.SetParent(this.transform);
            newApartment.Init(address, this);

            this.generatedApartments.Add(newApartment);
            this._clientApartments[address.doorNumber] = newApartment;
        }
    }

    private void OnDestroy() {
        if (NetworkServer.active) {
            SimpleTownNetwork.OnPlayerDisconnected -= RemoveDisconnectedPlayer;
        }
        // Remove from client registry
        BuildingBehavior.UnregisterClientHall(this.street, this.floorNumber);
    }

    // ── Client-side init ──────────────────────────────────────────────────────

    public void ClientSetup(string streetName, int floor) {
        this.street      = streetName;
        this.floorNumber = floor;

        if (this.geographicArea != null) {
            this.geographicArea.LocationText = $"{streetName}, Étage {floor}";
        }

        BuildingBehavior.RegisterClientHall(streetName, floor, this);

        // Client-only: instantiate the elevator. In host mode Init() already created it.
        if (!NetworkServer.active && this.elevator == null
            && this.elevatorPrefab != null && this.elevatorSpawn != null) {
            this.elevator = Instantiate(this.elevatorPrefab, this.elevatorSpawn.position, this.elevatorSpawn.rotation);
            this.elevator.transform.SetParent(this.transform);
            this.elevator.HallController = this;
            Debug.Log($"[Hall] Client elevator instantiated street={streetName} floor={floor}");
        }
    }

    /// <summary>
    /// Called from BuildingBehavior's S2C_ApartmentSpawn handler.
    /// Host mode: finds existing server-side apartment and calls ClientSetup.
    /// Client mode: instantiates the apartment prefab and calls ClientSetup.
    /// </summary>
    public void ClientSpawnApartment(S2C_ApartmentSpawn msg) {
        if (_clientApartments.TryGetValue(msg.DoorNumber, out ApartmentController existing)) {
            // Host mode: apartment already exists server-side, just set up client state
            existing.ClientSetup(msg.Street, msg.DoorNumber, msg.FloorNumber, msg.PresetName, this);
            return;
        }
        // Client-only mode: instantiate apartment prefab
        ApartmentController apt = Instantiate(talyahPrefab, msg.Position, msg.Rotation);
        apt.transform.SetParent(this.transform);
        apt.ClientSetup(msg.Street, msg.DoorNumber, msg.FloorNumber, msg.PresetName, this);
        _clientApartments[msg.DoorNumber] = apt;
    }

    /// <summary>
    /// Destroys client-side apartment instances (client-only mode).
    /// In host mode, apartments are destroyed via server-side Destroy().
    /// </summary>
    public void ClientDespawn() {
        if (!NetworkServer.active) {
            foreach (var apt in _clientApartments.Values) {
                if (apt != null) Destroy(apt.gameObject);
            }
        }
        _clientApartments.Clear();
    }

    // ── Server: apartment state checks ───────────────────────────────────────

    [Server]
    public void CheckApartmentState(int doorNumber) {
        ApartmentController apartmentTarget = GetApartmentByDoorNumber(doorNumber);
        // NOT_CREATED means Init() already started RetrieveData() — don't interrupt it with Regenerate().
        // Only regenerate if the previous load found no tenant (NOT_GENERATED) but a new player is now claiming it.
        if (apartmentTarget.State == ApartmentState.NOT_GENERATED) {
            Debug.Log($"Server: apartment {doorNumber} was NOT_GENERATED — regenerating for new tenant");
            apartmentTarget.Regenerate();
        } else {
            Debug.Log($"Server: apartment {doorNumber} state={apartmentTarget.State} — no regeneration needed");
        }
    }

    private ApartmentController GetApartmentByDoorNumber(int doorNumber) {
        ApartmentController apartmentTarget = this.generatedApartments.FirstOrDefault(x => x.Address.doorNumber.Equals(doorNumber));
        if (!apartmentTarget) {
            throw new Exception($"[HallController] Cannot move player to door number {doorNumber}");
        }
        return apartmentTarget;
    }

    // ── Server: player movement ───────────────────────────────────────────────

    /// <summary>
    /// Teleports an EXISTING (already-spawned) player to the hall elevator spawn.
    /// Called from BuildingBehavior.TeleportToFloor (elevator use).
    /// </summary>
    [Server]
    public void MoveToSpawn(NetworkConnectionToClient conn) {
        if (this.isGenerated) {
            Vector3 dest = GetElevatorSpawnPosition();
            Debug.Log($"[Hall] MoveToSpawn (isGenerated=true) immediate teleport conn={conn.connectionId} room={RoomId}");
            conn.Send(new TeleportMessage { destination = dest, NewRoomId = RoomId });
            this.playersInside.Add(conn);
        } else {
            // playerGo is null for existing players (already spawned)
            Debug.Log($"[Hall] MoveToSpawn (isGenerated=false) queued conn={conn.connectionId} room={RoomId} pendingApts={this.generatedApartments.Count}");
            this.playersToMove[conn] = (null, -1);
        }
    }

    private Vector3 GetElevatorSpawnPosition() {
        if (this.elevator != null && this.elevator.SpawnTransform != null)
            return this.elevator.SpawnTransform.position;
        // Fallback: elevator was placed at elevatorSpawn, use that transform directly.
        Debug.LogWarning($"[Hall] elevator.SpawnTransform is null for room={RoomId} — falling back to elevatorSpawn position");
        return this.elevatorSpawn != null ? this.elevatorSpawn.position : this.transform.position;
    }

    /// <summary>
    /// Called for a NEW player (playerGo not yet network-spawned).
    /// If the floor is ready: finalizes the spawn + teleports immediately.
    /// Otherwise: queues until CheckGenerationState fires.
    /// </summary>
    [Server]
    public void MoveToApartment(int doorNumber, NetworkConnectionToClient conn, GameObject playerGo) {
        if (this.isGenerated) {
            ApartmentController apartmentTarget = GetApartmentByDoorNumber(doorNumber);
            FinalizeAndTeleport(conn, playerGo, apartmentTarget.SpawnPosition.position, apartmentTarget.RoomId);
        } else {
            this.playersToMove[conn] = (playerGo, doorNumber);
        }
    }

    // Spawns the player (if not yet spawned) then sends TeleportMessage + UpdateCityDataMessage.
    [Server]
    private void FinalizeAndTeleport(NetworkConnectionToClient conn, GameObject playerGo,
                                     Vector3 destination, string roomId) {
        if (playerGo != null) {
            // New player — finalize network spawn first so OnStartLocalPlayer fires before
            // TeleportMessage is processed by the client.
            SimpleTownNetwork stn = Mirror.NetworkManager.singleton as SimpleTownNetwork;
            stn?.FinalizePlayerSpawn(conn, playerGo, spawnInCity: false);
        }

        conn.Send(new TeleportMessage { destination = destination, NewRoomId = roomId });
        this.playersInside.Add(conn);
    }

    [Server]
    public void CheckGenerationState() {
        // Guard: prevent double-broadcast when multiple apartment coroutines
        // complete in the same frame.
        if (_generationBroadcasted) return;

        int doneCount = this.generatedApartments
            .Where(x => x.State != ApartmentState.NOT_CREATED)
            .Count();
        this.isGenerated = doneCount == this.generatedApartments.Count;

        Debug.Log($"[Hall] CheckGenerationState room={RoomId} apartmentsDone={doneCount}/{this.generatedApartments.Count} isGenerated={this.isGenerated} pendingPlayers={this.playersToMove.Count}");

        if (!this.isGenerated) return;

        _generationBroadcasted = true;

        if (this.geographicArea != null) {
            this.geographicArea.LocationText = $"{this.street}, Étage {this.floorNumber}";
        }

        // Broadcast each apartment to all clients so they reconstruct the hall visually.
        int propCount = 0;
        foreach (ApartmentController apt in this.generatedApartments) {
            // Always send a preset name — use "talyah" as default for unoccupied apartments
            // so the client has geometry to show even when the apartment is NOT_GENERATED.
            string preset = (apt.State == ApartmentState.GENERATED && !string.IsNullOrEmpty(apt.PresetName))
                ? apt.PresetName
                : "talyah";

            NetworkServer.SendToAll(new S2C_ApartmentSpawn {
                Street      = this.street,
                FloorNumber = this.floorNumber,
                DoorNumber  = apt.Address.doorNumber,
                PresetName  = preset,
                Position    = apt.transform.position,
                Rotation    = apt.transform.rotation
            });
            propCount++;
            Debug.Log($"[Apartment] Registered apartment id={apt.Address.doorNumber} door={apt.Address.doorNumber} preset={preset}");
        }
        Debug.Log($"[RoomSnapshot] Floor {RoomId} fully generated — {propCount} apartments broadcast");

        // Finalize spawn + teleport for all waiting players.
        foreach (var kv in this.playersToMove) {
            NetworkConnectionToClient conn     = kv.Key;
            GameObject               playerGo  = kv.Value.playerGo;
            int                      doorNumber = kv.Value.doorNumber;

            if (doorNumber == -1) {
                // Elevator-path: teleport to spawn point (existing player, no GO).
                FinalizeAndTeleport(conn, playerGo, GetElevatorSpawnPosition(), RoomId);
            } else {
                ApartmentController apt = this.generatedApartments
                    .FirstOrDefault(x => x.Address.doorNumber == doorNumber);

                if (!apt) {
                    Debug.LogError($"[HallController] Cannot move player to door {doorNumber} — apartment not found");
                    continue;
                }

                Debug.Log($"[Hall] Spawning and teleporting player conn={conn.connectionId} to door={doorNumber} room={apt.RoomId}");
                FinalizeAndTeleport(conn, playerGo, apt.SpawnPosition.position, apt.RoomId);
            }
        }

        this.playersToMove.Clear();
    }

    /// <summary>
    /// Called by ApartmentController.Regenerate() so the hall can be re-generated
    /// and re-broadcast if a stale apartment triggers a re-fetch.
    /// </summary>
    [Server]
    public void OnApartmentRegenerating() {
        _generationBroadcasted = false;
        this.isGenerated = false;
    }

    public void RemovePlayer(NetworkIdentity networkIdentity) {
        if (networkIdentity?.connectionToClient != null)
            this.playersInside.Remove(networkIdentity.connectionToClient);
    }

    public bool ContainPlayers() {
        return this.playersInside.Count > 0 || this.playersToMove.Count > 0;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    public int FloorNumber => floorNumber;

    public string RoomId => $"hall:{street}:{floorNumber}";

    public TeleporterBehaviour Elevator => elevator;

    public BuildingBehavior AssociatedBuilding {
        get => associatedBuilding;
        set => associatedBuilding = value;
    }

    [Server]
    private void RemoveDisconnectedPlayer(NetworkConnectionToClient conn) {
        this.playersInside.Remove(conn);
        this.playersToMove.Remove(conn); // also clean up if they disconnected while waiting
        this.associatedBuilding?.TryToCleanHall(this);
    }
}
