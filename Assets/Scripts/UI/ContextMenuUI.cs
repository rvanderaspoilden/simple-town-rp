using System;
using System.Collections.Generic;
using Sim.Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Action = Sim.Interactables.Action;

namespace Sim.UI {
    public class ContextMenuUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private Button choiceButtonPrefab;

        [Header("Only for debug")]
        [SerializeField] private List<Button> buttonChoices;

        private void Awake() {
            this.buttonChoices = new List<Button>();
        }

        public void Setup(Interactable interactedProp) {
            // clear view
            this.buttonChoices.ForEach(button => button.onClick.RemoveAllListeners());
            this.buttonChoices.Clear();

            foreach (Transform child in this.transform) {
                Destroy(child.gameObject);
            }

            foreach (Action action in interactedProp.GetActions()) {
                Button button = Instantiate(this.choiceButtonPrefab, this.transform);
                button.interactable = !action.IsLocked();
                button.GetComponentInChildren<TextMeshProUGUI>().text = action.GetActionLabel();
                button.onClick.AddListener(() => interactedProp.DoAction(action));
                this.buttonChoices.Add(button);
            }
        }
    }
}
