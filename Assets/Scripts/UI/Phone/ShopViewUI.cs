using System;
using System.Linq;
using Sim;
using Sim.Enums;
using UnityEngine;

public class ShopViewUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Transform articleContainer;

    [SerializeField]
    private PhoneArticleCardUI phoneArticleCardPrefab;

    [SerializeField]
    private AudioClip buySuccessSound;

    public static ShopViewUI Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this);
        } else {
            Instance = this;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        DatabaseManager.PropsDatabase.GetProps().Where(config => config.GetPropsType() != PropsType.FOUNDATION && config.IsBuyable()).ToList().ForEach(config => {
            PhoneArticleCardUI card = Instantiate(this.phoneArticleCardPrefab, this.articleContainer);
            card.Setup(config);
        });
    }

    public void OnBuyResponse(bool isSuccess) {
        if (isSuccess) {
            HUDManager.Instance.PlaySound(this.buySuccessSound, .5f);
        }
    }
}
