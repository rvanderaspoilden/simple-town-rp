using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Enums;
using Sim.Scriptables;
using Sim.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    public class AdminPanelUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private Transform choiceContainer;
        [SerializeField] private Button buttonPrefab;

        [SerializeField] private CatalogCategoryEnum category;

        [Header("Only for debug")]
        [SerializeField] private List<Button> buttonChoices;

        public delegate void PropsClicked(PropsConfig config);

        public static event PropsClicked OnPropsClicked;
        
        public delegate void PaintClicked(PaintConfig config);

        public static event PaintClicked OnPaintClicked;

        private void Awake() {
            this.buttonChoices = new List<Button>();
        }

        private void OnEnable() {
            this.SetupChoices();
        }

        public void SetCategory(int categoryEnumValue) {
            this.category = (CatalogCategoryEnum)Enum.Parse(typeof(CatalogCategoryEnum), categoryEnumValue.ToString());
            this.SetupChoices();
        }

        public void SetupChoices() {
            // clear view
            this.buttonChoices.ForEach(button => {
                button.onClick.RemoveAllListeners();
                Destroy(button.gameObject);
            });
            this.buttonChoices.Clear();
            
            if (category == CatalogCategoryEnum.PROPS) {
                Resources.LoadAll<PropsConfig>("Configurations/Props").ToList().ForEach(config => {
                    Button button = Instantiate(this.buttonPrefab, this.choiceContainer);
                    button.GetComponentInChildren<TextMeshProUGUI>().text = config.GetDisplayName();
                    button.onClick.AddListener(() => OnPropsClicked?.Invoke(config));
                    this.buttonChoices.Add(button);
                });
            } else if (category == CatalogCategoryEnum.WALL_PAINT || category == CatalogCategoryEnum.GROUND_PAINT) {
                Resources.LoadAll<PaintConfig>("Configurations/Paints").ToList()
                    .Where(config => CommonUtils.ConvertBuildSurfaceToCategory(config.GetSurface()) == category)
                    .ToList()
                    .ForEach(config => {
                    Button button = Instantiate(this.buttonPrefab, this.choiceContainer);
                    button.GetComponentInChildren<TextMeshProUGUI>().text = config.GetDisplayName();
                    button.onClick.AddListener(() => OnPaintClicked?.Invoke(config));
                    this.buttonChoices.Add(button);
                });
            }
        }
    }
}