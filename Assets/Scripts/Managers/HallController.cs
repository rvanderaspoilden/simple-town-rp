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

    private HashSet<NetworkConnection> playersInside = new HashSet<NetworkConnection>();

    private string street;
    private int floorNumber;
    private bool isGenerated;

    private TeleporterBehaviour elevator;
    private BuildingBehavior associatedBuilding;

    private readonly HashSet<ApartmentController>     generatedApartments = new HashSet<ApartmentController>();
    private readonly Dictionary<NetworkConnection, int> playersToMove      = new Dictionary<NetworkConnection, int>();

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
        if (apartmentTarget.State != ApartmentState.GENERATED) {
            Debug.Log($"Server: apartment {doorNumber} isn't generated so create it");
            apartmentTarget.Regenerate();
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

    [Server]
    public void MoveToSpawn(NetworkConnectionToClient conn) {
        if (this.isGenerated) {
            conn.Send(new TeleportMessage {
                destination = this.elevator.SpawnTransform.position,
                NewRoomId   = RoomId
            });
            this.playersInside.Add(conn);
        } else {
            this.playersToMove.Add(conn, -1);
        }
    }

    [Server]
    public void MoveToApartment(int doorNumber, NetworkConnection conn) {
        if (this.isGenerated) {
            ApartmentController apartmentTarget = GetApartmentByDoorNumber(doorNumber);
            conn.Send(new TeleportMessage {
                destination = apartmentTarget.SpawnPosition.position,
                NewRoomId   = apartmentTarget.RoomId
            });
            this.playersInside.Add(conn);
        } else {
            this.playersToMove.Add(conn, doorNumber);
        }
    }

    [Server]
    public void CheckGenerationState() {
        this.isGenerated = this.generatedApartments
            .Where(x => x.State != ApartmentState.NOT_CREATED)
            .Count() == this.generatedApartments.Count;

        if (!this.isGenerated) return;

        if (this.geographicArea != null) {
            this.geographicArea.LocationText = $"{this.street}, Étage {this.floorNumber}";
        }

        // Broadcast each generated apartment to all clients
        foreach (ApartmentController apt in this.generatedApartments) {
            NetworkServer.SendToAll(new S2C_ApartmentSpawn {
                Street      = this.street,
                FloorNumber = this.floorNumber,
                DoorNumber  = apt.Address.doorNumber,
                PresetName  = apt.State == ApartmentState.GENERATED ? apt.PresetName : string.Empty,
                Position    = apt.transform.position,
                Rotation    = apt.transform.rotation
            });
        }

        // Move any waiting players
        foreach (KeyValuePair<NetworkConnection, int> entry in this.playersToMove) {
            TeleportMessage teleportMessage = new TeleportMessage {
                destination = this.elevator.SpawnTransform.position,
                NewRoomId   = RoomId
            };

            if (entry.Value != -1) {
                ApartmentController apartmentTarget = this.generatedApartments
                    .FirstOrDefault(x => x.Address.doorNumber.Equals(entry.Value));

                if (!apartmentTarget) {
                    throw new Exception($"[HallController] Cannot move player to door number {entry.Value}");
                }

                teleportMessage = new TeleportMessage {
                    destination = apartmentTarget.SpawnPosition.position,
                    NewRoomId   = apartmentTarget.RoomId
                };
            }

            entry.Key.Send(teleportMessage);
            this.playersInside.Add(entry.Key);
        }

        this.playersToMove.Clear();
    }

    public void RemovePlayer(NetworkIdentity networkIdentity) {
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
        this.associatedBuilding.TryToCleanHall(this);
    }
}
