using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mirror;
using Sim.Entities;
using Sim.Interactables;
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
    private int hallSpacingY = 100;

    [SerializeField]
    private Teleporter mainElevator;

    private Dictionary<int, HallController> hallControllerByFloor = new Dictionary<int, HallController>();

    public override void OnStartServer() {
        this.mainElevator.OnUse += TeleportToFloor;
    }

    public override void OnStopServer() {
        this.mainElevator.OnUse -= TeleportToFloor;
    }

    private int getFloorByDoorNumber(int doorNumber) {
        return Mathf.CeilToInt(doorNumber / (float)this.nbDoorByFloor);
    }

    public bool Match(Address address) {
        return this.streetName == address.street;
    }

    [Server]
    public void TeleportToApartment(int doorNumber, NetworkConnection conn) {
        Debug.Log($"Server: Teleport player to apartment {doorNumber}");

        HallController hallController = null;
        int targetFloor = this.getFloorByDoorNumber(doorNumber);

        if (hallControllerByFloor.ContainsKey(targetFloor)) {
            hallController = hallControllerByFloor[targetFloor];

            hallController.CheckApartmentState(doorNumber);
        } else {
            // CREATE FLOOR
            HallController newHallController = Instantiate(this.hallPrefab, new Vector3(0, -this.hallSpacingY * targetFloor, 0), Quaternion.identity);

            NetworkServer.Spawn(newHallController.gameObject);

            newHallController.Init(streetName, targetFloor);

            newHallController.Elevator.OnUse += TeleportToFloor;

            hallControllerByFloor.Add(targetFloor, newHallController);

            hallController = newHallController;
        }

        // TELEPORT PLAYER
        hallController.MoveToApartment(doorNumber, conn);
    }

    [ServerCallback]
    public void TeleportToFloor(int originFloor, int targetFloor, NetworkConnectionToClient conn) {
        Debug.Log($"Server: Teleport from {originFloor} to {targetFloor}");

        if (targetFloor == 0) {
            // TELEPORT PLAYER TO MAIN HALL
            conn.Send(new TeleportMessage {destination = this.mainElevator.SpawnTransform.position});
        } else {
            HallController hallController = null;

            if (hallControllerByFloor.ContainsKey(targetFloor)) {
                hallController = hallControllerByFloor[targetFloor];
            } else {
                // CREATE FLOOR
                HallController newHallController = Instantiate(this.hallPrefab, new Vector3(0, -this.hallSpacingY * targetFloor, 0), Quaternion.identity);

                NetworkServer.Spawn(newHallController.gameObject);

                newHallController.Init(streetName, targetFloor);

                newHallController.Elevator.OnUse += TeleportToFloor;

                hallControllerByFloor.Add(targetFloor, newHallController);

                hallController = newHallController;
            }

            // TELEPORT PLAYER
            hallController.MoveToSpawn(conn);
        }

        // Destroy hall if no players are in previous origin hall

        if (originFloor <= 0) return;
        
        if (hallControllerByFloor.ContainsKey(originFloor)) {
            hallControllerByFloor[originFloor].RemovePlayer(conn.identity);
                
            if (!hallControllerByFloor[originFloor].ContainPlayers()) {
                hallControllerByFloor[originFloor].Elevator.OnUse -= TeleportToFloor;
                NetworkServer.Destroy(hallControllerByFloor[originFloor].gameObject);
                hallControllerByFloor.Remove(originFloor);
            } else {
                Debug.Log($"Can't destroy hall with floor number {originFloor} because not empty");
            }
        } else {
            Debug.LogError($"Can't find hall with floor number {originFloor}");
        }
    }
}