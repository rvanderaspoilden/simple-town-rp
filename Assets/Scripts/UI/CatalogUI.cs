using System.Collections.Generic;
using System.Linq;
using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    public class CatalogUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private Button propsButtonPrefab;

        [Header("Only for debug")]
        [SerializeField] private List<Button> propsChoices;

        public delegate void PropsClicked(PropsConfig config);

        public static event PropsClicked OnPropsClicked;

        private void Awake() {
            this.propsChoices = new List<Button>();
        }

        private void OnEnable() {
            Resources.LoadAll<PropsConfig>("Configurations").ToList().ForEach(config => {
                Button propsButton = Instantiate(this.propsButtonPrefab, this.transform);
                propsButton.GetComponentInChildren<TextMeshProUGUI>().text = config.GetDisplayName();
                propsButton.onClick.AddListener(() => OnPropsClicked?.Invoke(config));
                this.propsChoices.Add(propsButton);
            });
        }

        private void OnDisable() {
            propsChoices.ForEach(button => {
                button.onClick.RemoveAllListeners();
                Destroy(button.gameObject);
            });
            propsChoices.Clear();
        }
    }
}