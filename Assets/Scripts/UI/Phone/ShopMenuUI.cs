using System.Collections.Generic;
using DG.Tweening;
using Sim;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class ShopMenuUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private VerticalLayoutGroup menuContainer;

    [SerializeField]
    private ShopMenuItem shopMenuItemPrefab;

    [SerializeField]
    private ShopCategoryConfig defaultCategoryConfig;

    [FormerlySerializedAs("shopViewUI")]
    [SerializeField]
    private ShopUI shopUI;

    [SerializeField]
    private Image menuBackground;

    private readonly List<ShopMenuItem> items = new List<ShopMenuItem>();

    private ShopCategoryConfig selectedCategoryConfig;

    private bool active;

    private Sequence itemFadeSequence;

    private void Awake() {
        this.menuBackground.transform.localScale = Vector3.one;
        SetVisibility(false);
        DatabaseManager.ShopCategoryConfigs.ForEach(config => {
            ShopMenuItem item = Instantiate(this.shopMenuItemPrefab, this.menuContainer.transform);
            item.Init(config, this);
            this.items.Add(item);
        });

        this.Select(this.items.Find(x => x.Config == defaultCategoryConfig));
    }

    private void SetVisibility(bool isVisible) {
        this.menuContainer.gameObject.SetActive(isVisible);

        if (isVisible) {
            this.itemFadeSequence?.Complete();

            this.itemFadeSequence = DOTween.Sequence();

            this.items.ForEach(item => { this.itemFadeSequence.Append(item.CanvasGroup.DOFade(1, .15f)); });

            this.itemFadeSequence.Play();
        } else {
            this.itemFadeSequence?.Complete();
            this.items.ForEach(item => item.CanvasGroup.alpha = 0);
        }
    }

    public void Select(ShopMenuItem item) {
        this.selectedCategoryConfig = item.Config;

        this.items.ForEach(x => { x.SetActive(x.Config == this.selectedCategoryConfig); });

        this.shopUI.ChangeCategory(this.selectedCategoryConfig);
    }

    public void Toggle() {
        this.active = !this.active;

        this.DoAnimation();
    }

    public void Close() {
        this.active = false;
        this.DoAnimation();
    }

    public void Open() {
        this.active = true;
        this.DoAnimation();
    }

    private void DoAnimation() {
        this.menuBackground.transform.DOComplete();

        if (this.active) {
            this.menuBackground.transform.DOScale(new Vector3(30, 30, 30), .3f).SetEase(Ease.Flash).OnComplete(() => SetVisibility(true));
        } else {
            SetVisibility(false);
            this.menuBackground.transform.DOScale(Vector3.one, .3f).SetEase(Ease.Flash);
        }
    }
}