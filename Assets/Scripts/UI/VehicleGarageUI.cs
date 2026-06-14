using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Modale "Mon garage" : liste les véhicules possédés que le joueur peut sortir. Autorée dans le
    /// HUD prefab (overlay + panneau + container vertical + ligne-template désactivée + bouton fermer).
    /// Ouverte par <see cref="HUDManager.ShowGarage"/> depuis <c>GarageDoor</c> ; chaque ligne
    /// dispo rappelle <c>onPick(vehicleId)</c> puis ferme.
    /// </summary>
    public class VehicleGarageUI : MonoBehaviour {
        [Tooltip("Conteneur (VerticalLayoutGroup) où les lignes sont instanciées.")]
        [SerializeField] private Transform listContainer;
        [Tooltip("Ligne modèle (désactivée) : Button + label TMP.")]
        [SerializeField] private GameObject rowTemplate;
        [Tooltip("Bouton de fermeture.")]
        [SerializeField] private Button closeButton;
        [Tooltip("Texte affiché quand le garage est vide.")]
        [SerializeField] private TextMeshProUGUI emptyLabel;

        public struct Entry { public string id; public string label; public bool available; }

        private Action<string> _onPick;

        private void Awake() {
            // NE PAS appeler SetActive(false) ici : l'objet démarre inactif (prefab), donc Awake
            // ne tourne qu'à la 1ʳᵉ activation (déclenchée par Show) — s'y refermer annulerait
            // la première ouverture. L'état masqué initial est porté par le prefab.
            if (rowTemplate != null) rowTemplate.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
        }

        public void Show(List<Entry> entries, Action<string> onPick) {
            _onPick = onPick;

            // Nettoie les anciennes lignes (sauf le template).
            if (listContainer != null) {
                foreach (Transform c in listContainer)
                    if (rowTemplate == null || c.gameObject != rowTemplate) Destroy(c.gameObject);
            }

            int count = entries != null ? entries.Count : 0;
            if (emptyLabel != null) emptyLabel.gameObject.SetActive(count == 0);

            if (listContainer != null && rowTemplate != null && entries != null) {
                foreach (Entry e in entries) {
                    GameObject row = Instantiate(rowTemplate, listContainer);
                    row.SetActive(true);
                    var label = row.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (label != null) label.text = e.available ? e.label : $"{e.label}  (dehors)";
                    var btn = row.GetComponent<Button>();
                    if (btn != null) {
                        btn.interactable = e.available;
                        string id = e.id;
                        btn.onClick.AddListener(() => { _onPick?.Invoke(id); Hide(); });
                    }
                }
            }

            gameObject.SetActive(true);
        }

        public void Hide() {
            gameObject.SetActive(false);
        }
    }
}
