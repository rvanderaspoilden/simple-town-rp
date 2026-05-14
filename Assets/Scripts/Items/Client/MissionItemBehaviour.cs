using UnityEngine;

/// <summary>
/// ItemBehaviour spécialisé pour les items spawn par le système Jobs
/// (colis livreur, outils éphémères…). Côté gameplay c'est un item normal —
/// la spécialisation est purement visuelle/UX pour signaler qu'il s'agit
/// d'une cible de mission.
///
/// Pour l'instant : affiche un outline (ou n'importe quel GameObject enfant
/// que tu drag dans le champ) quand l'item est au sol, le masque quand il
/// est en main.
/// </summary>
public class MissionItemBehaviour : ItemBehaviour
{
    [Header("Mission")]
    [Tooltip("GameObject visuel (outline, halo, badge) actif uniquement quand l'item est au sol.")]
    [SerializeField] private GameObject groundIndicator;

    protected override void Awake()
    {
        base.Awake();
        // État initial : posé au sol. Si le serveur dit IsHeld au spawn,
        // OnAttachedToHand sera appelé immédiatement et masquera l'indicator.
        SetIndicator(true);
    }

    public override void OnAttachedToHand(uint holderNetId, HandType hand)
    {
        base.OnAttachedToHand(holderNetId, hand);
        SetIndicator(false);
    }

    public override void OnDetachedFromHand()
    {
        base.OnDetachedFromHand();
        SetIndicator(true);
    }

    private void SetIndicator(bool visible)
    {
        if (groundIndicator != null) groundIndicator.SetActive(visible);
    }
}
