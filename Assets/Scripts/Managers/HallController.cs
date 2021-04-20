using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Entities;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;
using UnityEngine.AI;

public class HallController : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    private string street;

    [SerializeField]
    private NavMeshSurface navMeshSurface;

    [SerializeField]
    private Teleporter elevator;

    [Tooltip("Apartment controllers need to be well ordered")]
    [SerializeField]
    private ApartmentController[] apartmentControllers;

    [Header("Debug")]
    [SerializeField]
    private int floorNumber;

    [SerializeField]
    private bool isGenerated;

    private List<NetworkConnectionToClient> playersToMove = new List<NetworkConnectionToClient>();

    private void Start() {
        navMeshSurface.BuildNavMesh();
    }

    public override void OnStopServer() {
        base.OnStopServer();

        for (int i = 0; i < 1; i++) {
            this.apartmentControllers[i].OnApartmentGenerated -= OnApartmentGenerated;
        }
    }

    [Server]
    public void Init(int number) {
        this.floorNumber = number;
        
        for (int i = 0; i < 1; i++) {
            Address address = new Address {
                Street = this.street,
                DoorNumber = (i + 1) * this.floorNumber,
                HomeType = HomeTypeEnum.APARTMENT
            };

            this.apartmentControllers[i].OnApartmentGenerated += OnApartmentGenerated;

            this.apartmentControllers[i].Init(address);
        }
    }

    [Server]
    public void MoveToSpawn(NetworkConnectionToClient conn) {
        if (this.isGenerated) {
            conn.Send(new TeleportMessage {destination = this.elevator.SpawnTransform.position});
        } else {
            this.playersToMove.Add(conn);
        }
    }

    private void OnApartmentGenerated() {
        Debug.Log("Apartement has been generated");
        this.isGenerated = this.apartmentControllers.Where(x => x.IsGenerated).ToList().Count == this.apartmentControllers.Length;

        if (this.isGenerated) {
            Debug.Log("Hall is generated so teleport player");
            this.playersToMove.ForEach(player => player.Send(new TeleportMessage {destination = this.elevator.SpawnTransform.position}));
            this.playersToMove.Clear();
        }
    }
}