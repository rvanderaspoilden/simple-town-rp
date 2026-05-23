using UnityEngine;
using Sim.Jobs;

/// <summary>
/// Colis de mission (prefab "Job Package"). Spécialise <see cref="MissionItemBehaviour"/>
/// (qui gère l'outline au sol) en ajoutant une ÉTIQUETTE dont la COULEUR dépend de la
/// <see cref="SortingCategory"/> assignée au spawn.
///
/// La catégorie n'est pas portée par l'ItemConfig ni par le pipeline d'items générique :
/// elle est transmise au runtime par un message JOB dédié (JobSortItemsSpawnedMessage),
/// que JobClientManager applique en appelant SetSortingCategory sur ce composant.
///
/// L'étiquette est un SpriteRenderer enfant du carton (un seul sprite, le même pour
/// toutes les catégories). On lui applique juste la couleur du bac qui accepte cette
/// catégorie (<see cref="SortingBin.TryGetCategoryColor"/>) : le joueur n'a qu'à déposer
/// le colis dans le bac de la même couleur. Catégorie None / sans bac → pas d'étiquette.
///
/// NOTE : le SpriteRenderer n'est pas un MeshRenderer, donc MissionHighlightEffect ne le
/// déplace jamais sur la couche outline — l'étiquette n'est pas contourée, seul le carton l'est.
/// </summary>
public class PackageJobItemBehaviour : MissionItemBehaviour
{
    [Header("Étiquette colis")]
    [Tooltip("SpriteRenderer de l'étiquette collée sur le carton (enfant du prefab). " +
             "Sprite unique : seule sa couleur change selon la catégorie.")]
    [SerializeField] private SpriteRenderer stickerRenderer;

    public void SetSortingCategory(SortingCategory category)
    {
        if (stickerRenderer == null) return;

        if (category == SortingCategory.None)
        {
            stickerRenderer.enabled = false;
            return;
        }

        // Même couleur que le bac qui accepte cette catégorie → le joueur n'a qu'à
        // matcher la couleur. Pas de bac correspondant : on garde l'étiquette en blanc.
        Color color = SortingBin.TryGetCategoryColor(category, out var c) ? c : Color.white;
        stickerRenderer.color = color;
        stickerRenderer.enabled = true;
    }
}
