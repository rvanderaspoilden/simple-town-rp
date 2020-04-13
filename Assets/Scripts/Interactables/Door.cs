using System;
using Sim;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;

public class Door : Interactable {
    [Header("Door Settings")]
    [SerializeField] private PlacesEnum destination;

    public override void Interact() {
        base.Interact();
        
        NetworkManager.Instance.GoToRoom(destination);
    }
}
