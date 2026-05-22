using System;
using UnityEngine;

/// <summary>
/// Colis de mission (prefab "Job Package"). Spécialise <see cref="MissionItemBehaviour"/>
/// (qui gère l'outline au sol) en ajoutant une ÉTIQUETTE dont le sprite dépend de la
/// <see cref="SortingCategory"/> assignée au spawn.
///
/// La catégorie n'est pas portée par l'ItemConfig ni par le pipeline d'items générique :
/// elle est transmise au runtime par un message JOB dédié (JobSortItemsSpawnedMessage),
/// que JobClientManager applique en appelant SetSortingCategory sur ce composant.
///
/// L'étiquette est un SpriteRenderer enfant du carton, à configurer sur le prefab :
/// on assigne <see cref="stickerRenderer"/> et on renseigne le mapping
/// catégorie → sprite dans <see cref="stickers"/>. Catégorie None / non mappée → pas
/// d'étiquette (colis de livraison standard).
///
/// NOTE : le SpriteRenderer n'est pas un MeshRenderer, donc MissionHighlightEffect ne le
/// déplace jamais sur la couche outline — l'étiquette n'est pas contourée, seul le carton l'est.
/// </summary>
public class PackageJobItemBehaviour : MissionItemBehaviour
{
    [Serializable]
    private struct CategorySticker
    {
        public SortingCategory category;
        public Sprite sprite;
    }

    [Header("Étiquette colis")]
    [Tooltip("SpriteRenderer de l'étiquette collée sur le carton (enfant du prefab).")]
    [SerializeField] private SpriteRenderer stickerRenderer;

    [Tooltip("Sprite d'étiquette par catégorie de tri. Catégorie absente = pas d'étiquette.")]
    [SerializeField] private CategorySticker[] stickers = Array.Empty<CategorySticker>();

    public void SetSortingCategory(SortingCategory category)
    {
        if (stickerRenderer == null) return;

        Sprite sprite = ResolveSprite(category);
        if (sprite != null)
        {
            stickerRenderer.sprite = sprite;
            stickerRenderer.enabled = true;
        }
        else
        {
            stickerRenderer.enabled = false;
        }
    }

    private Sprite ResolveSprite(SortingCategory category)
    {
        if (stickers == null) return null;
        for (int i = 0; i < stickers.Length; i++)
        {
            if (stickers[i].category == category) return stickers[i].sprite;
        }
        return null;
    }
}
