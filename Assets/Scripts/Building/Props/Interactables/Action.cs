using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    [System.Serializable]
    public class Action {
        [SerializeField] private ActionTypeEnum actionType;
        [SerializeField] private string actionLabel;

        [SerializeField]
        private Sprite actionIcon;
        
        [Tooltip("If true => action is not possible")]
        [SerializeField] private bool locked;

        public Action(ActionTypeEnum type, string actionLabel, bool locked = false) {
            this.actionType = type;
            this.actionLabel = actionLabel;
            this.locked = locked;
        }

        public ActionTypeEnum GetActionType() {
            return this.actionType;
        }

        public string GetActionLabel() {
            return this.actionLabel;
        }

        public bool IsLocked() {
            return this.locked;
        }

        public void SetIsLocked(bool value) {
            this.locked = value;
        }

        public Sprite ActionIcon {
            get => actionIcon;
            set => actionIcon = value;
        }
    }

}