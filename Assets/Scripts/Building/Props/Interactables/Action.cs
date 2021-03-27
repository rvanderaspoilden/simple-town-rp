using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    [CreateAssetMenu(fileName = "New Action", menuName = "Configurations/Action")]
    public class Action : ScriptableObject {
        [SerializeField]
        private ActionTypeEnum type;

        [SerializeField]
        private string label;

        [SerializeField]
        private Sprite icon;

        [SerializeField]
        private bool needPermission;

        public delegate void ActionEvent(Action action);

        public event ActionEvent OnExecute;

        public ActionTypeEnum Type => type;

        public string Label => label;

        public Sprite Icon => icon;

        public bool NeedPermission => needPermission;

        public void Execute() {
            OnExecute?.Invoke(this);
        }
    }
}