using Mirror;
using UnityEngine;
using Sim.Jobs;

/// <summary>
/// ItemBehaviour spécialisé pour les items spawn par le système Jobs
/// (colis livreur, outils éphémères…). Côté gameplay c'est un item normal —
/// la spécialisation est purement visuelle/UX pour signaler qu'il s'agit
/// d'une cible de mission.
///
/// Outline piloté par deux conditions (et non plus seulement "au sol") :
///  • l'item est AU SOL (pas dans une main), ET
///  • le joueur local ne tient PAS déjà un colis de mission.
/// Ainsi, dès qu'on prend un colis, les autres colis au sol s'éteignent et on
/// ne voit plus que les bacs où déposer (gérés par MissionHighlightManager).
/// Mains libres : tous les colis au sol sont surlignés, les bacs masqués.
///
/// L'item informe le MissionHighlightManager quand le JOUEUR LOCAL le prend /
/// le lâche, et s'abonne à HoldStateChanged pour réagir aux changements
/// provoqués par les AUTRES colis.
/// </summary>
public class MissionItemBehaviour : ItemBehaviour
{
    private MissionHighlightEffect _highlightEffect;

    // Vrai si CET item est actuellement comptabilisé comme "tenu par le joueur
    // local" auprès du MissionHighlightManager. Évite les double-incréments.
    private bool _countedAsLocalHeld;

    protected override void Awake()
    {
        base.Awake();

        _highlightEffect = GetComponent<MissionHighlightEffect>();
        if (_highlightEffect == null) _highlightEffect = gameObject.AddComponent<MissionHighlightEffect>();

        MissionHighlightManager.HoldStateChanged += RefreshOutline;
        RefreshOutline();
    }

    protected override void OnDestroy()
    {
        // Despawn alors qu'on le tenait (ex. dépôt dans un bac) : libérer le compteur,
        // sinon LocalHoldsPayload resterait bloqué à true.
        ReportLocalHeld(false);
        MissionHighlightManager.HoldStateChanged -= RefreshOutline;
        base.OnDestroy();
    }

    public override void OnAttachedToHand(uint holderNetId, HandType hand)
    {
        base.OnAttachedToHand(holderNetId, hand);
        // Ne compter que si c'est le joueur LOCAL qui tient l'item.
        ReportLocalHeld(holderNetId == NetworkClient.connection?.identity?.netId);
        RefreshOutline();
    }

    public override void OnDetachedFromHand()
    {
        base.OnDetachedFromHand();
        ReportLocalHeld(false);
        RefreshOutline();
    }

    private void ReportLocalHeld(bool held)
    {
        if (held == _countedAsLocalHeld) return;
        _countedAsLocalHeld = held;
        MissionHighlightManager.SetLocalPayloadHeld(GetInstanceID(), held);
    }

    private void RefreshOutline()
    {
        if (_highlightEffect == null) return;
        // Surligné seulement s'il est au sol ET que le joueur local n'a pas déjà
        // un colis en main.
        bool show = !IsHeld && !MissionHighlightManager.LocalHoldsPayload;
        if (show) _highlightEffect.Show();
        else _highlightEffect.Hide();
    }
}
