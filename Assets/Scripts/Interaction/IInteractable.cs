using Sim.Interactables;
using UnityEngine;

namespace Interaction {
    public interface IInteractable {
        public float GetRange();

        public bool IsInteractable();

        public Action[] GetActions(bool withPriority = false);

        public void StopInteraction();
        public Transform transform { get; }
    }
}