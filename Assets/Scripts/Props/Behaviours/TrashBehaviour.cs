using System.Linq;
using Sim;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Poubelle : permet de jeter un sac poubelle (TrashBag, ItemConfig id 101) tenu en main.
/// L'action THROW n'apparaît que si le joueur local tient un sac. L'autorité reste serveur
/// (PropInteractionRouter.HandleTrash valide le holder + la config avant de despawn le sac).
/// </summary>
public class TrashBehaviour : PropBehaviourBase
{
    private const int TrashBagConfigId = 101;

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

        // THROW visible uniquement quand le joueur tient effectivement un sac poubelle.
        if (HeldTrashBagEntityId() < 0)
            acts = acts.Where(a => a.Type != ActionTypeEnum.THROW).ToArray();

        return acts;
    }

    protected override void Execute(Action action)
    {
        if (action.Type != ActionTypeEnum.THROW) return;

        int entityId = HeldTrashBagEntityId();
        if (entityId < 0) return;

        SendPropInteraction(PropType.Trash, TrashInteraction.ThrowRequest(entityId));
    }

    /// <summary>EntityId du sac poubelle tenu (gauche ou droite), ou -1 si aucun.</summary>
    private int HeldTrashBagEntityId()
    {
        PlayerHands hands = PlayerController.Local?.PlayerHands;
        if (hands == null) return -1;

        if (hands.LeftHandItem != null && hands.LeftHandItem.Configuration.ID == TrashBagConfigId)
            return hands.LeftEntityId;
        if (hands.RightHandItem != null && hands.RightHandItem.Configuration.ID == TrashBagConfigId)
            return hands.RightEntityId;

        return -1;
    }
}
