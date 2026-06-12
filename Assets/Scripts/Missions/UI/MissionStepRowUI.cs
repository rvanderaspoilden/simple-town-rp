using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Missions {
    /// <summary>
    /// Une ligne de la todo-list des steps dans le MissionActiveHUD : une case à cocher
    /// (cochée quand le step est complété) + le libellé du step. Le style change selon
    /// l'état : fait / en cours / à faire. Instanciée par <see cref="MissionActiveHUD"/> à
    /// partir d'un template.
    /// </summary>
    public class MissionStepRowUI : MonoBehaviour {
        [Header("Refs")]
        [Tooltip("Libellé du step.")]
        [SerializeField] private TMP_Text label;

        [Tooltip("Élément affiché quand le step est COMPLÉTÉ (la coche '✓'). Masqué sinon.")]
        [SerializeField] private GameObject checkMark;

        [Tooltip("Optionnel : graphique de la case (cadre) dont la couleur change selon l'état.")]
        [SerializeField] private Graphic box;

        [Header("Couleurs du libellé")]
        [SerializeField] private Color doneColor = new Color(1f, 1f, 1f, 0.45f);
        [SerializeField] private Color currentColor = Color.white;
        [SerializeField] private Color pendingColor = new Color(1f, 1f, 1f, 0.6f);

        [Header("Couleurs de la case")]
        [SerializeField] private Color boxDoneColor = new Color(0.30f, 0.78f, 0.35f, 1f);
        [SerializeField] private Color boxIdleColor = new Color(1f, 1f, 1f, 0.5f);

        /// <summary>Met à jour la ligne. <paramref name="done"/> = step complété, <paramref name="current"/> = step en cours.</summary>
        public void Set(string text, bool done, bool current) {
            if (label != null) {
                label.text = text;
                label.color = done ? doneColor : (current ? currentColor : pendingColor);
                label.fontStyle = current ? FontStyles.Bold : FontStyles.Normal;
            }
            if (checkMark != null) checkMark.SetActive(done);
            if (box != null) box.color = done ? boxDoneColor : boxIdleColor;
        }
    }
}
