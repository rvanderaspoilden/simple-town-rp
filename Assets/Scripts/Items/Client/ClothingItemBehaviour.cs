using UnityEngine;

/// <summary>
/// Item vêtement : deux visuels enfants exclusifs. TENU EN MAIN, il s'affiche toujours en pile
/// de linge plié (Prop_Clothes_Folded) ; POSÉ AU SOL, il montre son vrai modèle 3D (chaussures,
/// casque, lunettes…) quand le prefab en a un — les vêtements en tissu (hauts, pantalons) n'ont
/// pas de modèle sol et restent pliés partout. Porté (slot dos / wearables), le prefab entier
/// est de toute façon masqué (le visuel porté est l'élément APS2).
/// </summary>
public class ClothingItemBehaviour : ItemBehaviour
{
    [Header("Visuels (générés — Tools ▸ The BROZ ▸ APS2)")]
    [SerializeField, Tooltip("Pile de linge plié, affichée quand l'item est tenu en main.")]
    private GameObject foldedVisual;

    [SerializeField, Tooltip("Vrai modèle 3D affiché au sol. Null = tissu : plié au sol aussi.")]
    private GameObject modelVisual;

    public override void OnAttachedToHand(uint holderNetId, HandType hand)
    {
        base.OnAttachedToHand(holderNetId, hand);
        ApplyFoldedVisual(foldedVisual, modelVisual, heldInHand: hand != HandType.Back);
    }

    public override void OnDetachedFromHand()
    {
        base.OnDetachedFromHand();
        ApplyFoldedVisual(foldedVisual, modelVisual, heldInHand: false);
    }

    /// <summary>Bascule plié ↔ modèle (partagé avec <see cref="BackpackItemBehaviour"/>) : plié
    /// en main, modèle au sol — sauf item sans modèle (tissu), plié dans tous les cas.</summary>
    public static void ApplyFoldedVisual(GameObject folded, GameObject model, bool heldInHand)
    {
        bool showFolded = heldInHand || model == null;
        if (folded != null) folded.SetActive(showFolded);
        if (model != null) model.SetActive(!showFolded);
    }
}
