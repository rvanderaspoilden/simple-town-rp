using UnityEngine;
using Sim.Jobs;

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
    [Tooltip("ID de cible utilisé par le système de missions. S'il correspond à la cible active, l'item sera mis en évidence.")]
    [SerializeField] private string targetId;

    [Tooltip("GameObject visuel (outline, halo, badge) actif uniquement quand l'item est au sol.")]
    [SerializeField] private GameObject groundIndicator;

    protected override void Awake()
    {
        base.Awake();
        
        if (!string.IsNullOrEmpty(targetId)) {
            var effect = GetComponent<MissionHighlightEffect>();
            if (effect == null) effect = gameObject.AddComponent<MissionHighlightEffect>();
            MissionHighlightManager.Register(targetId, effect);
        }

        // État initial : posé au sol. Si le serveur dit IsHeld au spawn,
        // OnAttachedToHand sera appelé immédiatement et masquera l'indicator.
        SetIndicator(true);
    }

    protected virtual void OnDestroy() {
        if (!string.IsNullOrEmpty(targetId)) {
            var effect = GetComponent<MissionHighlightEffect>();
            if (effect != null) MissionHighlightManager.Unregister(targetId, effect);
        }
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
