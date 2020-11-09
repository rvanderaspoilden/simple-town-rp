using System.Collections.Generic;
using Sim.Enums;
using UnityEngine;

namespace Sim.Building {
    public class SimpleDoor : Props {
        [Header("Settings")]
        [SerializeField] private DoorDirectionEnum doorDirection = DoorDirectionEnum.FORWARD;
        
        private Animator animator;
        private List<Collider> colliderTriggered;

        protected override void Awake() {
            base.Awake();
            
            this.colliderTriggered = new List<Collider>();
            this.animator = GetComponent<Animator>();
            this.animator.SetFloat("direction", (float)doorDirection);
        }

        private void OnTriggerStay(Collider other) {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player") && !this.colliderTriggered.Find(x => x == other)) {
                this.colliderTriggered.Add(other);
            }
            
            this.UpdateAnimator();
        }

        private void OnTriggerExit(Collider other) {
            this.colliderTriggered.Remove(other);
            this.UpdateAnimator();
        }

        private void UpdateAnimator() {
            this.animator.SetBool("isOpened", this.colliderTriggered.Count > 0);
        }
    }
}

