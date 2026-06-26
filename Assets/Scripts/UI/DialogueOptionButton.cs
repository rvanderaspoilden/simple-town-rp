using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Un bouton de réponse du joueur dans la modale de dialogue (<see cref="DialogueUI"/>). Le
    /// GameObject porteur est un TEMPLATE désactivé autoré dans le prefab HUD ; la modale le clone
    /// une fois par réponse du nœud courant (cf. mémoire « author UI in prefab »).
    /// </summary>
    public class DialogueOptionButton : MonoBehaviour {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Button   button;

        public void Bind(string text, System.Action onClick) {
            if (this.label != null) this.label.text = text;

            if (this.button != null) {
                this.button.onClick.RemoveAllListeners();
                this.button.onClick.AddListener(() => onClick?.Invoke());
            }
        }
    }
}
