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

    private HashSet<NetworkIdentity> playersInside = new HashSet<NetworkIdentity>();

    [SyncVar]
    private string street;

    [SyncVar]
    private int floorNumber;

    [SyncVar(hook = nameof(OnGenerationFinished))]
    private bool isGenerated;

    private Teleporter elevator;

    private HashSet<ApartmentController> generatedApartments = new HashSet<ApartmentController>();

    private List<NetworkConnectionToClient> playersToMove = new List<NetworkConnectionToClient>();

    public void OnGenerationFinished(bool old, bool newValue) {
        this.isGenerated = newValue;
        this.geographicArea.LocationText = $"{this.street}, Floor {this.floorNumber}";
    }

    [Server]
    public void Init(string streetName, int floor) {
        this.floorNumber = floor;
        this.street = streetName;

        this.elevator = Instantiate(this.elevatorPrefab, this.elevatorSpawn.position, this.elevatorSpawn.rotation);

        this.elevator.transform.parent = this.transform;

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
    public void MoveToSpawn(NetworkConnectionToClient conn) {
        if (this.isGenerated) {
            conn.Send(new TeleportMessage {destination = this.elevator.SpawnTransform.position});
            this.playersInside.Add(conn.identity);
        } else {
            this.playersToMove.Add(conn);
        }
    }

    [Server]
    public void CheckGenerationState() {
        this.isGenerated = this.generatedApartments.Where(x => x.IsGenerated).ToList().Count == this.generatedApartments.Count;

        if (this.isGenerated) {
            Debug.Log("Hall is generated so teleport player");
            this.playersToMove.ForEach(player => {
                player.Send(new TeleportMessage {destination = this.elevator.SpawnTransform.position});
                this.playersInside.Add(player.identity);
            });
            this.playersToMove.Clear();
        }
    }

    public void RemovePlayer(NetworkIdentity networkIdentity) => this.playersInside.Remove(networkIdentity);

    public bool ContainPlayers() => this.playersInside.Count > 0 || this.playersToMove.Count > 0;

    public int FloorNumber => floorNumber;

    public Teleporter Elevator => elevator;
}