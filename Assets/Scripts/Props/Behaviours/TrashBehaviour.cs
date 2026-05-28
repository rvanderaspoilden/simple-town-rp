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
        PlayThrowVfx();

        if (byLocalPlayer)
            WorldToastManager.Show("🌱 Quartier plus propre", "+1 Crédit Social ⭐", delay: 0.35f);
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

        // Un seul item → on le jette directement.
        if (held.Count == 1) { SendThrow(held[0].entityId); return; }

        // Plusieurs items → menu radial de choix (réutilise le HUD contextuel). Différé
        // d'une frame : le menu radial courant se ferme juste après cet Execute, donc on
        // rouvre le menu de choix à la frame suivante pour qu'il ne soit pas balayé.
        var choices = new List<Action>();
        foreach (var h in held)
        {
            int eid = h.entityId;
            ItemConfig cfg = h.item != null ? h.item.Configuration : null;
            Action choice = Action.CreateRuntime(
                ActionTypeEnum.THROW,
                cfg != null ? cfg.Label : "Jeter",
                cfg != null ? cfg.Icon : null);
            choice.OnExecute += _ => SendThrow(eid);
            choices.Add(choice);
        }
        StartCoroutine(ShowChoiceMenuNextFrame(choices.ToArray()));
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
