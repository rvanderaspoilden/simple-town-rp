using System;
using System.Collections.Generic;
using System.Linq;
using Sim;
using UnityEngine;
using UnityEngine.UI;

public class ShopListView : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private GameObject itemListContainer;

    [SerializeField]
    private VerticalLayoutGroup verticalLayoutGroup;

    [SerializeField]
    private PhoneArticleCardUI articleCardPrefab;

    [SerializeField]
    private PhoneCoverCardUI coverCardPrefab;

    private List<PhoneCardUI> items = new List<PhoneCardUI>();

    public void Setup(ShopCategoryConfig config) {
        if (items.Count > 0) {
            foreach (var item in items) {
                Destroy(item.gameObject);
            }

            items.Clear();
        }

        if (config.CategoryType == ShopCategoryType.PROPS) {
            DatabaseManager.PropsDatabase.GetProps().Where(x => x.GetPropsType().Equals(config.PropsType) && x.IsBuyable()).ToList().ForEach(x => {
                PhoneArticleCardUI card = Instantiate(this.articleCardPrefab, this.verticalLayoutGroup.transform);
                card.Setup(x);
                items.Add(card);
            });
        } else {
            DatabaseManager.PaintDatabase.GetPaints().Where(x => x.GetSurface().Equals(config.CoverType) && x.IsBuyable()).ToList().ForEach(x => {
                PhoneCoverCardUI card = Instantiate(this.coverCardPrefab, this.verticalLayoutGroup.transform);
                card.Setup(x);
                items.Add(card);
            });
        }

        this.Show();
    }

    public void Filter(string itemName) {
        this.items.ForEach(item => { item.gameObject.SetActive(item.GetDisplayName().ToLower().IndexOf(itemName.ToLower(), StringComparison.Ordinal) != -1); });
    }

    public void Show() {
        this.itemListContainer.SetActive(true);
    }

    public void Hide() {
        this.itemListContainer.SetActive(false);
    }
}