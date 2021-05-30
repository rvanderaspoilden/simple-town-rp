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
    private CanvasGroup menuBackground;

    private readonly List<ShopMenuItem> items = new List<ShopMenuItem>();

    private ShopCategoryConfig selectedCategoryConfig;

    private bool active;

    private bool hover;

    private bool isAnimating;

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

    public void Hover(bool isHover) {
        this.hover = isHover;

        if (this.active || this.isAnimating) return;

        this.menuBackground.DOComplete();
        this.menuBackground.DOFade(isHover ? 1f : 0f, .3f);
    }

    public void Close() {
        this.active = false;
        this.DoAnimation();
    }

    public void Open() {
        this.active = true;
        this.DoAnimation();
    }

    public bool Active => active;

    private void DoAnimation() {
        this.menuBackground.transform.DOComplete();
        this.menuBackground.DOComplete();

        this.isAnimating = true;

        if (this.active) {
            this.menuBackground.DOFade(1f, .3f);
            this.menuBackground.transform.DOScale(new Vector3(30, 30, 30), .3f).SetEase(Ease.Flash).OnComplete(() => {
                SetVisibility(true);
                this.isAnimating = false;
            });
        } else {
            SetVisibility(false);

            this.menuBackground.transform.DOScale(Vector3.one, .3f).SetEase(Ease.Flash).OnComplete(() => {
                if (!hover) {
                    this.menuBackground.DOFade(0f, .3f);
                }
                
                this.isAnimating = false;
            });
        }
    }
}