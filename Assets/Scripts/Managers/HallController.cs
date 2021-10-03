using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Entities;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;

public class HallController : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    private ApartmentController talyahPrefab;

    [SerializeField]
    private Transform[] apartmentSpawnPoints;

    [SerializeField]
    private Teleporter elevatorPrefab;

    [SerializeField]
    private Transform elevatorSpawn;

    [SerializeField]
    private GeographicArea geographicArea;

    private HashSet<NetworkConnection> playersInside = new HashSet<NetworkConnection>();

    [SyncVar]
    private string street;

    [SyncVar]
    private int floorNumber;

    [SyncVar(hook = nameof(OnGenerationFinished))]
    private bool isGenerated;

    private Teleporter elevator;

    private BuildingBehavior associatedBuilding;

    private HashSet<ApartmentController> generatedApartments = new HashSet<ApartmentController>();

    private Dictionary<NetworkConnection, int> playersToMove = new Dictionary<NetworkConnection, int>();

    public override void OnStartServer() {
        base.OnStartServer();

        SimpleTownNetwork.OnPlayerDisconnected += RemoveDisconnectedPlayer;
    }

    public override void OnStopServer() {
        base.OnStopServer();
            
        SimpleTownNetwork.OnPlayerDisconnected -= RemoveDisconnectedPlayer;
    }
    
    public void OnGenerationFinished(bool old, bool newValue) {
        this.isGenerated = newValue;
        this.geographicArea.LocationText = $"{this.street}, Floor {this.floorNumber}";
    }

    [Server]
    public void Init(string streetName, int floor, BuildingBehavior building) {
        this.floorNumber = floor;
        this.street = streetName;
        this.associatedBuilding = building;

        this.elevator = Instantiate(this.elevatorPrefab, this.elevatorSpawn.position, this.elevatorSpawn.rotation);

        this.elevator.transform.parent = this.transform;

        this.elevator.HallController = this;

        this.elevator.ParentId = netId;

        NetworkServer.Spawn(this.elevator.gameObject);
        for (int i = 0; i < this.apartmentSpawnPoints.Length; i++) {
            Address address = new Address {
                street = this.street,
                doorNumber = (i + 1) + (this.apartmentSpawnPoints.Length * (this.floorNumber - 1)),
                homeType = HomeTypeEnum.APARTMENT
            };

            ApartmentController newApartment = Instantiate(this.talyahPrefab, this.apartmentSpawnPoints[i].position, this.apartmentSpawnPoints[i].rotation);

            newApartment.transform.SetParent(this.transform);

            newApartment.ParentId = netId;

            NetworkServer.Spawn(newApartment.gameObject);

            newApartment.Init(address, this);

            this.generatedApartments.Add(newApartment);
        }
    }

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

    [Server]
    public void MoveToSpawn(NetworkConnectionToClient conn) {
        if (this.isGenerated) {
            conn.Send(new TeleportMessage {destination = this.elevator.SpawnTransform.position});
            this.playersInside.Add(conn);
        } else {
            this.playersToMove.Add(conn, -1);
        }
    }

    [Server]
    public void MoveToApartment(int doorNumber, NetworkConnection conn) {
        if (this.isGenerated) {
            ApartmentController apartmentTarget = GetApartmentByDoorNumber(doorNumber);

            conn.Send(new TeleportMessage {destination = apartmentTarget.SpawnPosition.position});
            this.playersInside.Add(conn);
        } else {
            this.playersToMove.Add(conn, doorNumber);
        }
    }

    [Server]
    public void CheckGenerationState() {
        this.isGenerated = this.generatedApartments.Where(x => x.State != ApartmentState.NOT_CREATED).ToList().Count == this.generatedApartments.Count;

        if (this.isGenerated) {
            foreach (KeyValuePair<NetworkConnection, int> entry in this.playersToMove) {
                TeleportMessage teleportMessage = new TeleportMessage {destination = this.elevator.SpawnTransform.position};

                if (entry.Value != -1) {
                    ApartmentController apartmentTarget = this.generatedApartments.FirstOrDefault(x => x.Address.doorNumber.Equals(entry.Value));

                    if (!apartmentTarget) {
                        throw new Exception($"[HallController] Cannot move player to door number {entry.Value}");
                    }

                    teleportMessage = new TeleportMessage {destination = apartmentTarget.SpawnPosition.position};
                }

                entry.Key.Send(teleportMessage);
                this.playersInside.Add(entry.Key);
            }

            this.playersToMove.Clear();
        }
    }

    public void RemovePlayer(NetworkIdentity networkIdentity) {
        this.playersInside.Remove(networkIdentity.connectionToClient);
    }

    public bool ContainPlayers() {
        return this.playersInside.Count > 0 || this.playersToMove.Count > 0;
    }

    public int FloorNumber => floorNumber;

    public Teleporter Elevator => elevator;

    public BuildingBehavior AssociatedBuilding {
        get => associatedBuilding;
        set => associatedBuilding = value;
    }
    
    [Server]
    private void RemoveDisconnectedPlayer(int connId) {
        this.playersInside = new HashSet<NetworkConnection>(this.playersInside.Where(x => x != null && x.connectionId != connId).ToList());
        
        this.associatedBuilding.TryToCleanHall(this);
    }
}