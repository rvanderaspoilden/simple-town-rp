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

        private bool _forbidden;

        public delegate void ActionEvent(Action action);

        public event ActionEvent OnExecute;

        public ActionTypeEnum Type => type;

        public string Label => label;

        public Sprite Icon => icon;

        public bool NeedPermission => needPermission;

        public bool IsForbidden {
            get => _forbidden;
            set => _forbidden = value;
        }

        public void Execute() {
            OnExecute?.Invoke(this);
        }

        /// <summary>
        /// Crée une Action runtime (non sérialisée) avec type/label/icône donnés. Utile
        /// pour des choix dynamiques affichés dans le menu radial (ex. choisir quel item
        /// jeter dans une poubelle). S'abonner à OnExecute pour le callback.
        /// </summary>
        public static Action CreateRuntime(ActionTypeEnum type, string label, Sprite icon) {
            Action a = CreateInstance<Action>();
            a.type = type;
            a.label = label;
            a.icon = icon;
            return a;
        }
    }
}