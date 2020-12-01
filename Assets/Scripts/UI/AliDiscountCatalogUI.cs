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
    public class AliDiscountCatalogUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Transform choiceContainer;

        [SerializeField]
        private Transform categoryContainer;

        [SerializeField]
        private Button itemButtonPrefab;

        [SerializeField]
        private Button categoryButtonPrefab;

        [SerializeField]
        private CatalogCategoryEnum category;

        [Header("Only for debug")]
        [SerializeField]
        private List<Button> buttonChoices;

        public delegate void PropsClicked(PropsConfig config);

        public static event PropsClicked OnPropsClicked;

        public delegate void PaintClicked(PaintConfig config);

        public static event PaintClicked OnPaintClicked;

        private void Awake() {
            this.buttonChoices = new List<Button>();
            this.GenerateCategories();
        }

        private void OnEnable() {
            this.SetupChoices();
        }

        public void SetCategory(CatalogCategoryEnum category) {
            this.category = category;
            this.SetupChoices();
        }

        private void GenerateCategories() {
            foreach (int enumValue in Enum.GetValues(typeof(CatalogCategoryEnum))) {
                Button categoryButton = Instantiate(this.categoryButtonPrefab, this.categoryContainer);
                categoryButton.GetComponentInChildren<TextMeshProUGUI>().text = ((CatalogCategoryEnum) enumValue).ToString();
                categoryButton.onClick.AddListener(() => this.SetCategory((CatalogCategoryEnum) enumValue));
            }
        }

        private void SetupChoices() {
            this.ClearChoices();

            if (category == CatalogCategoryEnum.FURNITURE) {
                DatabaseManager.PropsDatabase.GetProps().Where(config => config.GetPropsType() != PropsType.FOUNDATION).ToList().ForEach(config => {
                    Button button = Instantiate(this.itemButtonPrefab, this.choiceContainer);
                    button.GetComponentInChildren<TextMeshProUGUI>().text = config.GetDisplayName();
                    button.onClick.AddListener(() => OnPropsClicked?.Invoke(config));
                    this.buttonChoices.Add(button);
                });
            } else {
                DatabaseManager.PaintDatabase.GetPaints()
                    .Where(config => CommonUtils.ConvertBuildSurfaceToCategory(config.GetSurface()) == category)
                    .ToList()
                    .ForEach(config => {
                        Button button = Instantiate(this.itemButtonPrefab, this.choiceContainer);
                        button.GetComponentInChildren<TextMeshProUGUI>().text = config.GetDisplayName();
                        button.onClick.AddListener(() => OnPaintClicked?.Invoke(config));
                        this.buttonChoices.Add(button);
                    });
            }
        }

        private void ClearChoices() {
            this.buttonChoices.ForEach(button => {
                button.onClick.RemoveAllListeners();
                Destroy(button.gameObject);
            });
            this.buttonChoices.Clear();
        }
    }
}