using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim.Constellation {
    // Écoute la touche d'ouverture (K) et toggle la constellation via le HUDManager.
    // Ignoré pendant un sub-game ou quand un champ de saisie a le focus (pour ne pas
    // intercepter la frappe « k » dans le chat / la recherche).
    public class ConstellationInput : MonoBehaviour {
        [SerializeField] private KeyCode toggleKey = KeyCode.K;

        private void Update() {
            if (!Input.GetKeyDown(toggleKey)) return;
            if (SubGameController.IsActive) return;
            if (IsTypingInField()) return;
            if (HUDManager.Instance == null) return;

            HUDManager.Instance.ToggleConstellation();
        }

        private static bool IsTypingInField() {
            var es = EventSystem.current;
            if (es == null) return false;
            var sel = es.currentSelectedGameObject;
            return sel != null && sel.GetComponent<TMP_InputField>() != null;
        }
    }
}
