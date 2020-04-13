using System;
using Photon.Pun;
using UnityEngine;

namespace Sim.Interactables {
    public class Interactable : MonoBehaviourPun {
        [Header("Basic settings")]
        [SerializeField] private Sprite interactionCursor; // Todo create scriptable object
        [SerializeField] private float rangeToInteract; // Todo create scriptable object

        public virtual void Interact() {
            Debug.Log("Interaction with " + this.name);
        }

        public virtual bool CanInteractWithPlayer(Player player) {
            Collider[] players = Physics.OverlapSphere(this.transform.position, this.rangeToInteract, (1 << 8));
            
            for (int i = 0; i < players.Length; i++) {
                if (players[i].GetComponent<Player>() == player) {
                    return true;
                }
            }

            return false;
        }
    }   
}
