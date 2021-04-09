using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Entities;
using Sim.Enums;
using UnityEngine;
using UnityEngine.AI;

public class HallController : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    private string street;

    [SerializeField]
    private NavMeshSurface navMeshSurface;

    [SerializeField]
    private Transform spawnPosition;

    [Tooltip("Apartment controllers need to be well ordered")]
    [SerializeField]
    private ApartmentController[] apartmentControllers;

    [Header("Debug")]
    [SerializeField]
    private int floorNumber;

    [SerializeField]
    private bool isGenerated;

    private List<GameObject> playersToMove = new List<GameObject>();

    private void Start() {
        navMeshSurface.BuildNavMesh();
    }

    private void OnDestroy() {
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
    public void MoveToSpawn(GameObject player) {
        this.playersToMove.Add(player);
    }

    private void OnApartmentGenerated(bool isGenerated) {
        Debug.Log("Apartement has been generated");
        this.isGenerated = this.apartmentControllers.Where(x => x.IsGenerated).ToList().Count == this.apartmentControllers.Length;

        if (this.isGenerated) {
            Debug.Log("Hall is generated so teleport player");
            this.playersToMove.ForEach(player => {
                player.GetComponent<NavMeshAgent>().enabled = false;
                player.transform.position = this.spawnPosition.position;
                player.GetComponent<NavMeshAgent>().enabled = true;
            });
            this.playersToMove.Clear();
        }
    }

    public Transform SpawnPosition => spawnPosition;
}