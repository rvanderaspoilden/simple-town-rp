using UnityEngine;
using Sim.Jobs;

/// <summary>
/// ItemBehaviour spécialisé pour les items spawn par le système Jobs
/// (colis livreur, outils éphémères…). Côté gameplay c'est un item normal —
/// la spécialisation est purement visuelle/UX pour signaler qu'il s'agit
/// d'une cible de mission.
///
/// Règle : un item de mission est toujours surligné tant qu'il est AU SOL
/// (pas dans les mains). Contrairement aux props de carrière (machine, bac)
/// qui sont pilotés par le step courant via MissionHighlightManager, le colis
/// pilote lui-même son outline sur son état "tenu / posé" — il doit rester
/// repérable même pendant un step de déplacement (Reach/Deliver).
/// </summary>
public class MissionItemBehaviour : ItemBehaviour
{
    private MissionHighlightEffect _highlightEffect;

    protected override void Awake()
    {
        base.Awake();

        _highlightEffect = GetComponent<MissionHighlightEffect>();
        if (_highlightEffect == null) _highlightEffect = gameObject.AddComponent<MissionHighlightEffect>();

        // État initial : posé au sol → outline visible. Si le serveur le spawn
        // directement en main, OnAttachedToHand le masquera juste après.
        _highlightEffect.Show();
    }

    public override void OnAttachedToHand(uint holderNetId, HandType hand)
    {
        base.OnAttachedToHand(holderNetId, hand);
        // Item en main → on masque l'outline mission tant qu'il est porté.
        if (_highlightEffect != null) _highlightEffect.Hide();
    }

    public override void OnDetachedFromHand()
    {
        base.OnDetachedFromHand();
        // Reposé au sol → on rallume l'outline pour qu'il reste repérable.
        if (_highlightEffect != null) _highlightEffect.Show();
    }
}
