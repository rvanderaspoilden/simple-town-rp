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
    
    // Start is called before the first frame update
    void Start()
    {
        DatabaseManager.PropsDatabase.GetProps().Where(config => config.GetPropsType() != PropsType.FOUNDATION && config.IsBuyable()).ToList().ForEach(config => {
            PhoneArticleCardUI card = Instantiate(this.phoneArticleCardPrefab, this.articleContainer);
            card.Setup(config);
        });
    }
}
