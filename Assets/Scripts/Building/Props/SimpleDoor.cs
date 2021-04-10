using System.Collections.Generic;
using Sim.Enums;
using UnityEngine;

namespace Sim.Building {
    public class SimpleDoor : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private DoorDirectionEnum doorDirection = DoorDirectionEnum.FORWARD;

        [SerializeField]
        private List<Collider> colliderTriggered;

        private Animator animator;

        private bool isOpened;

        private int directionHash;
        private int isOpenedHash;

        protected void Awake() {
            this.directionHash = Animator.StringToHash("direction");
            this.isOpenedHash = Animator.StringToHash("isOpened");

            this.colliderTriggered = new List<Collider>();
            this.animator = GetComponent<Animator>();
            this.animator.SetFloat(this.directionHash, (float) doorDirection);
        }

        private void OnTriggerEnter(Collider other) {
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
            this.isOpened = this.colliderTriggered.Count > 0;

            if (this.animator.GetBool(this.isOpenedHash) != this.isOpened) {
                this.animator.SetBool(this.isOpenedHash, this.isOpened);
            }
        }
    }
}