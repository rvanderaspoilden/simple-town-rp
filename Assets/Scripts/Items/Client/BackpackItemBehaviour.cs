using UnityEngine;

/// <summary>
/// Item sac à dos : même bascule de visuels que <see cref="ClothingItemBehaviour"/> (pile de
/// linge plié en main, vrai modèle 3D au sol), mais hérite de <see cref="PackageItemBehaviour"/>
/// pour garder l'ouverture de la grille (action OPEN, place item_container). Porté sur le dos,
/// le prefab entier est masqué — le visuel porté est l'élément APS2 Item1.
/// </summary>
public class BackpackItemBehaviour : PackageItemBehaviour
{
    [Header("Visuels (générés — Tools ▸ The BROZ ▸ APS2)")]
    [SerializeField, Tooltip("Pile de linge plié, affichée quand le sac est tenu en main.")]
    private GameObject foldedVisual;

    [SerializeField, Tooltip("Vrai modèle 3D du sac, affiché au sol.")]
    private GameObject modelVisual;

    public override void OnAttachedToHand(uint holderNetId, HandType hand)
    {
        base.OnAttachedToHand(holderNetId, hand);
        ClothingItemBehaviour.ApplyFoldedVisual(foldedVisual, modelVisual, heldInHand: hand != HandType.Back);
    }

    public override void OnDetachedFromHand()
    {
        base.OnDetachedFromHand();
        ClothingItemBehaviour.ApplyFoldedVisual(foldedVisual, modelVisual, heldInHand: false);
    }
}
