using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Poubelle : permet de jeter dans la poubelle n'importe quel item tenu en main.
/// L'action THROW apparaît dès que le joueur local tient au moins un item. S'il en tient
/// deux, un menu radial de choix s'ouvre pour sélectionner lequel jeter. L'autorité reste
/// serveur (PropInteractionRouter.HandleTrash valide que l'item est bien tenu et n'est pas
/// un item de mission avant de le despawn).
/// </summary>
public class TrashBehaviour : PropBehaviourBase
{
    [Header("Trash VFX")]
    [Tooltip("VFX eco joué quand un sac est jeté. Optionnel : si vide, chargé depuis Resources/VFX/VFX_TrashEco.")]
    [SerializeField] private GameObject throwVfxPrefab;
    [Tooltip("Décalage local (depuis le pivot du prop) où le VFX émerge — typiquement l'ouverture de la poubelle.")]
    [SerializeField] private Vector3 vfxLocalOffset = new Vector3(0f, 0.7f, 0f);

    private static GameObject _cachedVfxPrefab;
    private static bool _vfxPrefabLoaded;

    /// <summary>
    /// Feedback complet d'un jet réussi. Le VFX joue pour tous les clients de la room ;
    /// le toast informatif n'apparaît que pour le joueur qui a jeté (feedback local),
    /// légèrement après le VFX.
    /// </summary>
    public void OnThrown(bool byLocalPlayer)
    {
        // VFX eco pour tous les clients de la room. La récompense (points Environnement)
        // + le toast de succès sont gérés côté serveur (PropInteractionRouter.HandleTrash),
        // qui les envoie au seul jeteur — d'où plus de toast local ici.
        PlayThrowVfx();
        Sim.Audio.AudioManager.Instance.Play(Sim.Audio.SfxId.TrashThrow, transform.position);
    }

    /// <summary>
    /// Joue le VFX de validation eco à l'ouverture de la poubelle. Appelé sur TOUS les
    /// clients de la room via ClientPropManager.OnTrashThrown (purement cosmétique).
    /// </summary>
    public void PlayThrowVfx()
    {
        GameObject prefab = ResolveVfxPrefab();
        if (prefab == null) return;

        Vector3 spawnPos = transform.TransformPoint(vfxLocalOffset);
        GameObject vfx = Instantiate(prefab, spawnPos, Quaternion.identity);

        // Auto-nettoyage : durée du VFX (0.8s) + marge pour les dernières particules.
        Destroy(vfx, 2f);
    }

    private GameObject ResolveVfxPrefab()
    {
        if (throwVfxPrefab != null) return throwVfxPrefab;
        if (!_vfxPrefabLoaded)
        {
            _cachedVfxPrefab = Resources.Load<GameObject>("VFX/VFX_TrashEco");
            _vfxPrefabLoaded = true;
            if (_cachedVfxPrefab == null)
                Debug.LogWarning("[TrashBehaviour] VFX prefab introuvable : Resources/VFX/VFX_TrashEco");
        }
        return _cachedVfxPrefab;
    }

    public override Action[] GetActions(bool withPriority = false)
    {
        Action[] acts = base.GetActions(withPriority);
        bool hasHeld = HasAnyHeldItem();

        // si le joueur tient au moins un item.
        acts = acts.Where(a => (a.Type != ActionTypeEnum.THROW || hasHeld))
                   .ToArray();

        // En clic gauche prioritaire : si JETER est disponible, c'est l'action exécutée
        // directement (pas besoin d'ouvrir le radial pour faire le geste évident).
        if (withPriority && hasHeld) {
            Action throwAct = acts.FirstOrDefault(a => a.Type == ActionTypeEnum.THROW);
            if (throwAct != null) return new[] { throwAct };
        }

        return acts;
    }

    protected override void Execute(Action action)
    {
        if (action.Type != ActionTypeEnum.THROW) return;

        PlayerHands hands = PlayerController.Local?.PlayerHands;
        if (hands == null) return;

        // Collecte les items tenus (gauche + droite).
        var held = new List<(int entityId, ItemBehaviour item)>();
        if (hands.LeftHandItem != null)  held.Add((hands.LeftEntityId,  hands.LeftHandItem));
        if (hands.RightHandItem != null) held.Add((hands.RightEntityId, hands.RightHandItem));

        if (held.Count == 0) return;

        // Un seul item → on le jette directement (confirmation si c'est un colis).
        if (held.Count == 1) { RequestThrow(held[0].entityId, held[0].item); return; }

        // Plusieurs items → menu radial de choix (réutilise le HUD contextuel). Différé
        // d'une frame : le menu radial courant se ferme juste après cet Execute, donc on
        // rouvre le menu de choix à la frame suivante pour qu'il ne soit pas balayé.
        var choices = new List<Action>();
        foreach (var h in held)
        {
            int eid = h.entityId;
            ItemBehaviour itm = h.item;
            ItemConfig cfg = itm != null ? itm.Configuration : null;
            Action choice = Action.CreateRuntime(
                ActionTypeEnum.THROW,
                cfg != null ? cfg.Label : "Jeter",
                cfg != null ? cfg.Icon : null);
            choice.OnExecute += _ => RequestThrow(eid, itm);
            choices.Add(choice);
        }
        StartCoroutine(ShowChoiceMenuNextFrame(choices.ToArray()));
    }

    /// <summary>
    /// Jette l'item — mais demande confirmation d'abord si c'est un item-conteneur (colis),
    /// car le serveur supprime alors définitivement le colis ET tout son contenu.
    /// </summary>
    private void RequestThrow(int entityId, ItemBehaviour item)
    {
        ItemConfig cfg = item != null ? item.Configuration : null;
        bool isContainer = Sim.Scriptables.ItemContainerConfig.Of(cfg)?.IsContainer == true;

        if (isContainer) {
            string label = !string.IsNullOrEmpty(cfg.Label) ? cfg.Label : "ce colis";
            Sim.UI.ConfirmDialogUI.Request(
                "Jeter le colis ?",
                $"« {label} » et tout son contenu seront définitivement supprimés. Cette action est irréversible.",
                () => SendThrow(entityId));
        } else {
            SendThrow(entityId);
        }
    }

    private IEnumerator ShowChoiceMenuNextFrame(Action[] choices)
    {
        yield return null; // laisse le menu radial courant se fermer
        if (HUDManager.Instance != null)
            HUDManager.Instance.ShowContextMenu(choices, transform);
    }

    private void SendThrow(int entityId)
    {
        SendPropInteraction(PropType.Trash, TrashInteraction.ThrowRequest(entityId));
    }

    /// <summary>Vrai si le joueur local tient au moins un item (gauche ou droite).</summary>
    private bool HasAnyHeldItem()
    {
        PlayerHands hands = PlayerController.Local?.PlayerHands;
        return hands != null && (hands.LeftHandItem != null || hands.RightHandItem != null);
    }
}
