using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Missions {
    /// <summary>
    /// Petite carte (post-it) affichée sur le board physique. Volontairement
    /// minimaliste : un fond coloré selon le statut (dispo / en cours) + un titre
    /// court. But : savoir en un coup d'œil qu'il y a des missions, pas lire un détail.
    ///
    /// Construite intégralement au runtime par <see cref="MissionBoardDisplay"/> (pas de
    /// prefab à câbler). Non interactive : aucun raycast UI, ne bloque pas le clic
    /// physique sur le board.
    /// </summary>
    public class MissionBoardCardView : MonoBehaviour {
        private Image _background;
        private TextMeshProUGUI _title;

        public static MissionBoardCardView Create(Transform parent, int titleFontSize) {
            var go = new GameObject("JobCard", typeof(RectTransform), typeof(Image), typeof(MissionBoardCardView));
            go.transform.SetParent(parent, false);

            var view = go.GetComponent<MissionBoardCardView>();
            view._background = go.GetComponent<Image>();
            view._background.raycastTarget = false;

            var textGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 10f);
            trt.offsetMax = new Vector2(-10f, -10f);

            view._title = textGo.GetComponent<TextMeshProUGUI>();
            view._title.fontSize = titleFontSize;
            view._title.alignment = TextAlignmentOptions.Center;
            view._title.color = Color.black;
            view._title.raycastTarget = false;

            return view;
        }

        public void Bind(string title, Color background) {
            if (_background != null) _background.color = background;
            if (_title != null) _title.text = title;
        }
    }
}
