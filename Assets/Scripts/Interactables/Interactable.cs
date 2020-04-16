using System;
using Photon.Pun;
using UnityEngine;

namespace Sim.Interactables {
    public class Interactable : MonoBehaviourPun {
        [Header("Basic settings")]
        [SerializeField] private float rangeToInteract; // Todo create scriptable object

        public virtual void Interact() {
            Debug.Log("Interaction with " + this.name);
        }

        public virtual bool CanInteract(Vector3 target) {
            return Mathf.Abs(Vector3.Distance(this.transform.position, target)) <= this.rangeToInteract;
        }
    }   
}
