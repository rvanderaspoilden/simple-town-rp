using UnityEngine;

/// <summary>
/// Marqueur posé sur un prop d'exposition d'un magasin physique (scène City).
/// Sa présence transforme le prop en article toujours en vente : le serveur le
/// marque ForSale au prix du PropsConfig (remisé), et l'achat crée une COPIE
/// livrée à l'acheteur — le prop d'expo reste en place (stock infini).
///
/// Présent en scène, donc disponible côté serveur (ServerPropManager) ET client
/// (PropBehaviourBase injecte l'action BUY).
/// </summary>
public class ShopDisplay : MonoBehaviour {
    [SerializeField, Range(0, 100), Tooltip("Remise en pourcentage appliquée sur le prix du PropsConfig. Ignoré si overridePrice >= 0.")]
    private int discountPercent = 0;

    [SerializeField, Tooltip("Prix fixe qui remplace totalement le prix du PropsConfig. -1 = non défini (on utilise le prix config remisé par discountPercent).")]
    private int overridePrice = -1;

    /// <summary>
    /// Prix de vente effectif : l'override l'emporte s'il est défini, sinon on
    /// applique la remise en pourcentage sur le prix de base du PropsConfig.
    /// </summary>
    public int EffectivePrice(int basePrice) {
        if (overridePrice >= 0) return overridePrice;
        return Mathf.RoundToInt(basePrice * (100 - Mathf.Clamp(discountPercent, 0, 100)) / 100f);
    }
}
