using System.Linq;
using Mirror;
using Sim;
using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private ItemSlot leftHandSlot;
    [SerializeField] private ItemSlot rightHandSlot;
    [SerializeField] private ItemSlot bothHandSlot;
    [SerializeField] private ItemSlot leftPocketSlot;
    [SerializeField] private ItemSlot rightPocketSlot;

    [SerializeField] private Transform     poolContainer;
    [SerializeField] private DraggableItem draggableItemPrefab;

    [SerializeField] private InventoryActionMenu leftHandActionMenu;
    [SerializeField] private InventoryActionMenu rightHandActionMenu;

    [Header("Only for debug")]
    [SerializeField] private InventoryActionMenu currentActionMenu;

    private GenericPool<DraggableItem> _draggableItemPool;

    private void Awake()
    {
        EnsurePool();

        // Les slots mains autorisent le swap hand↔hand (prédiction locale via Swap()).
        // Les poches aussi (pocket↔pocket, pocket↔hand, pocket↔container).
        // Les slots conteneur gardent CanSwap=false par défaut (activé dans ContainerPanelUI).
        if (leftHandSlot)    leftHandSlot.CanSwap    = true;
        if (rightHandSlot)   rightHandSlot.CanSwap   = true;
        if (bothHandSlot)    bothHandSlot.CanSwap    = true;
        if (leftPocketSlot)  { leftPocketSlot.CanSwap  = true; leftPocketSlot.SlotIndex  = 0; }
        if (rightPocketSlot) { rightPocketSlot.CanSwap = true; rightPocketSlot.SlotIndex = 1; }
    }

    /// <summary>
    /// Crée le pool si pas encore fait. Permet à <see cref="RentDraggable"/> /
    /// <see cref="ReleaseDraggable"/> d'être appelés avant le premier OnEnable de
    /// cette UI (ex. ContainerPanelUI loue un draggable alors que le panneau
    /// inventaire n'a jamais été affiché → Awake pas encore passé en hiérarchie inactive).
    /// </summary>
    private void EnsurePool()
    {
        if (_draggableItemPool != null) return;
        _draggableItemPool = new GenericPool<DraggableItem>(
            OnCreateDraggableItem, OnGetDraggableItem, OnReleaseDraggableItem);
    }

    private void OnEnable()
    {
        UpdateUI();

        DraggableItem.OnLeftClick   += OnItemLeftClicked;
        DraggableItem.OnRightClick  += OnItemRightClicked;
        DraggableItem.OnDoubleClick += OnItemDoubleClicked;
        DraggableItem.OnStartDrag   += OnItemStartDrag;
        ItemSlot.OnItemMove         += OnItemMoved;
        ItemSlot.OnItemSwap         += OnItemsSwapped;
        PlayerHands.OnHandChanged   += OnPlayerHandChanged;
        ClientItemManager.MoveItemResult += OnMoveItemResultReceived;
        ClientItemManager.PocketSync     += OnPocketSyncReceived;

        // Replay du dernier snapshot poche : la session est ouverte serveur-side au
        // connect ; les opens/closes de la HUD ne re-déclenchent pas de S2C_PocketSync.
        if (ClientItemManager.LastPocketSnapshot.HasValue)
            ApplyPocketSnapshot(ClientItemManager.LastPocketSnapshot.Value);
    }

    private void OnDisable()
    {
        // Release par slot — NE PAS pool.Dispose() car le pool est aussi consommé par
        // ContainerPanelUI, et un Dispose() global libérerait ses draggables aussi.
        ReleaseSlot(leftHandSlot);
        ReleaseSlot(rightHandSlot);
        ReleaseSlot(bothHandSlot);

        DraggableItem.OnLeftClick   -= OnItemLeftClicked;
        DraggableItem.OnRightClick  -= OnItemRightClicked;
        DraggableItem.OnDoubleClick -= OnItemDoubleClicked;
        DraggableItem.OnStartDrag   -= OnItemStartDrag;
        ItemSlot.OnItemMove         -= OnItemMoved;
        ItemSlot.OnItemSwap         -= OnItemsSwapped;
        PlayerHands.OnHandChanged   -= OnPlayerHandChanged;
        ClientItemManager.MoveItemResult -= OnMoveItemResultReceived;
        ClientItemManager.PocketSync     -= OnPocketSyncReceived;

        // Ferme le conteneur avec l'inventaire (même comportement que si le joueur
        // clique le bouton fermer du container ou appuie sur Escape).
        ContainerPanelUI.Instance?.Close();
    }

    // ── API pool partagée (consommée par ContainerPanelUI) ────────────────────

    /// <summary>Loue un DraggableItem du pool. Le caller le reparente sur son slot via SetItem.</summary>
    public DraggableItem RentDraggable() { EnsurePool(); return _draggableItemPool.Get(); }

    /// <summary>Rend un DraggableItem au pool (reparente sur le pool container + désactive).</summary>
    public void ReleaseDraggable(DraggableItem item) {
        if (item == null) return;
        EnsurePool();
        _draggableItemPool.Release(item);
    }

    /// <summary>
    /// Libère TOUS les <see cref="DraggableItem"/> enfants du transform du slot et
    /// vide <c>slot._item</c>. On itère les enfants (et pas juste <c>slot.Item</c>)
    /// pour balayer les orphelins issus de swaps visuels où un draggable a été
    /// reparenté sans être tracké correctement — sans ce nettoyage les orphelins
    /// s'empilent au même anchored (0,0) et cassent le drag&drop en pile.
    /// </summary>
    private void ReleaseSlot(ItemSlot slot) {
        if (slot == null) return;
        var t = slot.transform;
        for (int c = t.childCount - 1; c >= 0; c--) {
            var d = t.GetChild(c).GetComponent<DraggableItem>();
            if (d != null) _draggableItemPool.Release(d);
        }
        slot.Clear();
    }

    private void Update()
    {
        if (SubGameController.IsActive) return;

        if (Input.GetKeyDown(KeyCode.Escape))
            HUDManager.Instance.CloseInventory();

        // Ferme l'inventaire (et donc le container via OnDisable) dès que le joueur
        // commence à marcher — cohérent avec la règle "HUD fermé pendant le mouvement".
        var agent = PlayerController.Local?.NavMeshAgent;
        if (agent != null && agent.velocity.sqrMagnitude > 0.01f)
            HUDManager.Instance.CloseInventory();
    }

    private void OnPlayerHandChanged()
    {
        Debug.Log("[InventoryUI] [OnPlayerHandChanged] Refreshing all slots");
        CloseCurrentActionMenu(true);
        UpdateUI();
    }

    private void OnItemLeftClicked(DraggableItem draggableItem)   => CloseCurrentActionMenu();
    private void OnItemStartDrag(DraggableItem draggableItem)     => CloseCurrentActionMenu();

    /// <summary>
    /// Double-clic gauche : quick-move main/poche → conteneur ouvert. Le sens conteneur
    /// → inventaire est géré par <see cref="ContainerPanelUI.OnItemDoubleClicked"/>.
    /// </summary>
    private void OnItemDoubleClicked(DraggableItem item)
    {
        if (item == null || item.ItemSlot == null) return;
        var src = item.ItemSlot;
        bool srcIsOurs = src == leftHandSlot || src == rightHandSlot || src == bothHandSlot
                      || src == leftPocketSlot || src == rightPocketSlot;
        if (!srcIsOurs) return;

        var container = ContainerPanelUI.Instance;
        if (container == null || !container.IsOpen) return;     // pas de conteneur ouvert → no-op

        int targetSlot = container.FindFirstFreeSlot();
        if (targetSlot < 0) {
            WorldToastManager.Show("Conteneur plein");
            return;
        }
        if (string.IsNullOrEmpty(container.PlaceId) || string.IsNullOrEmpty(src.PlaceId)) return;
        if (!NetworkClient.isConnected) return;

        NetworkClient.Send(new C2S_MoveItem {
            EntityId    = item.EntityId,
            FromPlaceId = src.PlaceId,
            ToPlaceId   = container.PlaceId,
            ToSlotIndex = targetSlot,
        });
    }

    /// <summary>
    /// Choisit une destination "main/poche" libre pour un quick-move depuis un conteneur.
    /// Priorité : main droite → main gauche → poche slot 0 → poche slot 1. Pour un item
    /// TWO_HAND, n'autorise que les mains, et seulement si les DEUX sont libres.
    /// Retourne le placeKey wire ("hand_right:{charId}" / "pocket:{charId}") + slotIndex,
    /// ou false si rien de libre n'est compatible.
    /// </summary>
    public bool TryFindQuickMoveTargetForItem(DraggableItem item, out string placeKey, out int slotIndex)
    {
        placeKey = null; slotIndex = 0;
        if (PlayerController.Local == null) return false;
        string charId = PlayerController.Local.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) return false;

        var hands = PlayerController.Local.PlayerHands;
        ItemConfig cfg = item != null ? item.ItemConfig : null;
        bool isTwoHand = cfg != null && cfg.HandleType == ItemHandleType.TWO_HAND;

        if (isTwoHand) {
            // TWO_HAND : besoin des DEUX mains libres ; sinon abandonne (les poches ne
            // peuvent pas accueillir un objet à deux mains).
            if (hands.RightHandItem == null && hands.LeftHandItem == null) {
                placeKey = $"hand_right:{charId}";
                return true;
            }
            return false;
        }

        if (hands.RightHandItem == null) { placeKey = $"hand_right:{charId}"; return true; }
        if (hands.LeftHandItem  == null) { placeKey = $"hand_left:{charId}";  return true; }

        if (leftPocketSlot != null && leftPocketSlot.Item == null) {
            placeKey = $"pocket:{charId}"; slotIndex = 0; return true;
        }
        if (rightPocketSlot != null && rightPocketSlot.Item == null) {
            placeKey = $"pocket:{charId}"; slotIndex = 1; return true;
        }
        return false;
    }

    private void OnItemRightClicked(DraggableItem draggableItem)
    {
        if (draggableItem.ItemSlot == leftHandSlot)
        {
            if (currentActionMenu == leftHandActionMenu) CloseCurrentActionMenu();
            else DisplayLeftActionMenu();
        }
        else if (draggableItem.ItemSlot == rightHandSlot || draggableItem.ItemSlot == bothHandSlot)
        {
            // Pour un item TWO_HAND, l'instance est posée en bothHandSlot ;
            // les actions sont sur RightHandItem dans ce cas.
            if (currentActionMenu == rightHandActionMenu) CloseCurrentActionMenu();
            else DisplayRightActionMenu();
        }
    }

    private void OnItemMoved(ItemSlot originSlot, ItemSlot targetSlot)
    {
        // Drop vers un slot de destination différent : route via C2S_MoveItem.
        // Le hand↔hand swap est désormais émis via OnItemSwap (OnItemsSwapped).
        string fromPlace = originSlot.PlaceId;
        string toPlace   = targetSlot.PlaceId;
        if (string.IsNullOrEmpty(fromPlace) || string.IsNullOrEmpty(toPlace)) return;
        // fromPlace == toPlace : hand-swap (left↔right) déjà géré au-dessus ; reorder
        // intra-container (même UUID, slot différent) → laisse passer. Même slot → skip.
        if (fromPlace == toPlace && originSlot.SlotIndex == targetSlot.SlotIndex) return;

        var placeholder = targetSlot.Item;
        int entityId = placeholder != null ? placeholder.EntityId : 0;
        if (entityId <= 0) {
            Debug.LogWarning("[InventoryUI] OnItemMoved : draggable sans EntityId, skip C2S_MoveItem");
            return;
        }
        if (!NetworkClient.isConnected) return;

        NetworkClient.Send(new C2S_MoveItem {
            EntityId    = entityId,
            FromPlaceId = fromPlace,
            ToPlaceId   = toPlace,
            ToSlotIndex = targetSlot.SlotIndex,
        });

        // Le draggable dans targetSlot est un placeholder : le panneau autoritaire
        // (Container/Pocket/Hand) repopulera proprement via le snapshot/S2C_SpawnItem
        // qui arrive juste après. On doit le release pour ne pas qu'il coexiste avec
        // le draggable de remplacement.
        //
        // MAIS le release SYNCHRONE tuerait le tween de drop en cours (ResetDragState
        // → StopCoroutine sur _moveTween). On délaie donc le release de la durée du
        // land tween, ce qui laisse l'animation se jouer. Le pool.Release étant
        // idempotent, si le snapshot a déjà libéré le placeholder entre-temps c'est
        // un no-op safe.
        targetSlot.Clear();
        if (placeholder != null && placeholder.LandDuration > 0f) {
            StartCoroutine(DeferredReleaseCoroutine(placeholder, placeholder.LandDuration + 0.02f));
        } else {
            ReleaseDraggable(placeholder);
        }
    }

    private System.Collections.IEnumerator DeferredReleaseCoroutine(DraggableItem d, float delay)
    {
        yield return new UnityEngine.WaitForSecondsRealtime(delay);
        if (d == null) yield break;
        // Si entre-temps un snapshot serveur a recyclé ce draggable depuis le pool
        // pour repeupler un slot (ItemSlot.SetItem affecte d.ItemSlot ET slot._item),
        // le draggable est désormais utilisé légitimement → on ne le release pas.
        // Sans ce guard, on kickerait l'item authoritatif hors de son slot juste
        // après que le snapshot l'a placé.
        if (d.ItemSlot != null && d.ItemSlot.Item == d) yield break;
        ReleaseDraggable(d);
    }

    /// <summary>
    /// Swap visuel déjà appliqué dans ItemSlot.OnDrop. On route :
    ///  • hand↔hand   → PlayerHands.Swap() (C2S_RequestSwapHands)
    ///  • le reste    → C2S_SwapItems (un seul aller-retour serveur, deux PATCH atomiques).
    /// Sur échec serveur le snapshot S2C_ContainerOpened re-poussé reconciliera le panneau,
    /// et OnMoveItemResultReceived déclenchera UpdateUI() pour les mains.
    /// </summary>
    private void OnItemsSwapped(ItemSlot slotA, DraggableItem itemA, ItemSlot slotB, DraggableItem itemB)
    {
        bool isHandPair = (slotA == leftHandSlot  && slotB == rightHandSlot)
                       || (slotA == rightHandSlot && slotB == leftHandSlot);
        if (isHandPair) {
            PlayerController.Local.PlayerHands.Swap();
            return;
        }

        if (string.IsNullOrEmpty(slotA.PlaceId) || string.IsNullOrEmpty(slotB.PlaceId)) return;
        int entityA = itemA != null ? itemA.EntityId : 0;
        int entityB = itemB != null ? itemB.EntityId : 0;
        if (entityA <= 0 || entityB <= 0) {
            Debug.LogWarning("[InventoryUI] OnItemsSwapped : entity ids manquants, refresh UI");
            UpdateUI();
            return;
        }
        if (!NetworkClient.isConnected) return;

        NetworkClient.Send(new C2S_SwapItems {
            EntityIdA  = entityA,
            PlaceIdA   = slotA.PlaceId,
            SlotIndexA = slotA.SlotIndex,
            EntityIdB  = entityB,
            PlaceIdB   = slotB.PlaceId,
            SlotIndexB = slotB.SlotIndex,
        });
    }

    private void OnPocketSyncReceived(S2C_PocketSync msg) => ApplyPocketSnapshot(msg);

    /// <summary>
    /// Reconstruit complètement les slots poche à partir du snapshot serveur :
    /// libère les anciens draggables, en rente de nouveaux pour chaque item.
    /// </summary>
    private void ApplyPocketSnapshot(S2C_PocketSync msg)
    {
        // Garde-fou : si OnEnable est appelé avant Awake dans un scénario non standard
        // (cas d'un GameObject inactif dans le prefab puis activé par un autre script),
        // le pool peut être null. EnsurePool est idempotent.
        EnsurePool();

        Debug.Log($"[InventoryUI] ApplyPocketSnapshot placeId={msg.PlaceId} slotCount={msg.SlotCount} items={msg.Items?.Length ?? 0}");

        // Vide d'abord ce qui occupe les slots poche (release vers le pool partagé) :
        // robuste face aux états intermédiaires (drag visuel d'un autre slot vers la
        // poche déjà appliqué mais snapshot pas encore reçu) — l'autorité est le snapshot
        // serveur, on libère toujours ce qui occupe la poche AVANT de re-peupler.
        ReleasePocketSlot(leftPocketSlot);
        ReleasePocketSlot(rightPocketSlot);

        if (msg.Items == null) return;
        foreach (var entry in msg.Items) {
            ItemSlot target = entry.SlotIndex == 0 ? leftPocketSlot
                            : entry.SlotIndex == 1 ? rightPocketSlot
                            : null;
            if (target == null) {
                Debug.LogWarning($"[InventoryUI] Pocket entry slotIndex={entry.SlotIndex} hors range (0-1) ou slot UI non câblé, ignoré.");
                continue;
            }
            var cfg = DatabaseManager.GetItemConfigById(entry.ConfigId);
            if (cfg == null) {
                Debug.LogWarning($"[InventoryUI] Pocket ItemConfig {entry.ConfigId} introuvable, slot {entry.SlotIndex} ignoré.");
                continue;
            }
            DraggableItem d = _draggableItemPool.Get();
            d.SetConfiguration(cfg);
            d.SetEntityId(entry.EntityId);
            target.SetItem(d);
        }
    }

    private void ReleasePocketSlot(ItemSlot slot)
    {
        if (slot == null) return;
        // Même logique défensive que ReleaseSlot : balaie tous les DraggableItem
        // enfants pour éviter l'accumulation d'orphelins issus de swaps hand↔pocket.
        var t = slot.transform;
        for (int c = t.childCount - 1; c >= 0; c--) {
            var d = t.GetChild(c).GetComponent<DraggableItem>();
            if (d != null) _draggableItemPool.Release(d);
        }
        slot.Clear();
    }

    private void OnMoveItemResultReceived(S2C_MoveItemResult msg) {
        if (msg.Success) return;
        // Server a rejeté le move : feedback joueur via toast + ré-affichage de l'état
        // authoritatif (le placeholder a déjà été release, l'origin slot était cleared
        // par OnDrop, UpdateUI re-rente un draggable depuis PlayerHands si l'item y est encore).
        Debug.LogWarning($"[InventoryUI] Move refusé entity={msg.EntityId} reason={msg.ErrorMessage} — refresh UI");
        if (!string.IsNullOrEmpty(msg.ErrorMessage)) WorldToastManager.Show(msg.ErrorMessage);
        UpdateUI();
    }

    public void DisplayLeftActionMenu()
    {
        CloseCurrentActionMenu();
        var item = PlayerController.Local.PlayerHands.LeftHandItem;
        if (item == null) return;
        var actions = item.GetActions().ToList();
        Debug.Log($"[InventoryUI] LeftHand actions count={actions.Count} for config={item.Configuration?.Label}");
        leftHandActionMenu.Setup(actions);
        currentActionMenu = leftHandActionMenu;
    }

    public void DisplayRightActionMenu()
    {
        CloseCurrentActionMenu();
        var item = PlayerController.Local.PlayerHands.RightHandItem;
        if (item == null) return;
        var actions = item.GetActions().ToList();
        Debug.Log($"[InventoryUI] RightHand actions count={actions.Count} for config={item.Configuration?.Label}");
        rightHandActionMenu.Setup(actions);
        currentActionMenu = rightHandActionMenu;
    }

    public void CloseCurrentActionMenu(bool instantly = false)
    {
        if (!currentActionMenu) return;
        currentActionMenu.Hide(instantly);
        currentActionMenu = null;
    }

    public void UpdateUI()
    {
        // Release uniquement les slots mains/poches — pas pool.Dispose() global, qui
        // libérerait aussi les draggables hébergés ailleurs (ex. ContainerPanelUI).
        ReleaseSlot(leftHandSlot);
        ReleaseSlot(rightHandSlot);
        ReleaseSlot(bothHandSlot);

        if (PlayerController.Local == null) return;

        // Pose les PlaceId sur les slots mains/poches dès qu'on connaît le character id.
        // Permet à OnItemMoved de router un drag vers/depuis ces slots via C2S_MoveItem.
        string charId = PlayerController.Local.CharacterData?.Id;
        if (!string.IsNullOrEmpty(charId)) {
            leftHandSlot.PlaceId   = $"hand_left:{charId}";
            rightHandSlot.PlaceId  = $"hand_right:{charId}";
            bothHandSlot.PlaceId   = $"hand_right:{charId}";
            if (leftPocketSlot)  leftPocketSlot.PlaceId  = $"pocket:{charId}";
            if (rightPocketSlot) rightPocketSlot.PlaceId = $"pocket:{charId}";
        }

        var hands = PlayerController.Local.PlayerHands;
        ItemBehaviour rightItem = hands.RightHandItem;
        ItemBehaviour leftItem  = hands.LeftHandItem;
        int leftEntityId  = hands.LeftEntityId;
        int rightEntityId = hands.RightEntityId;

        if (rightItem != null && rightItem.Configuration.HandleType == ItemHandleType.TWO_HAND)
        {
            leftHandSlot.gameObject.SetActive(false);
            rightHandSlot.gameObject.SetActive(false);
            bothHandSlot.gameObject.SetActive(true);

            DraggableItem draggable = _draggableItemPool.Get();
            draggable.SetConfiguration(rightItem.Configuration);
            draggable.SetEntityId(rightEntityId);
            bothHandSlot.SetItem(draggable);
            Debug.Log("[InventoryUI] Refresh slot BothHands (two-hand item)");
        }
        else
        {
            leftHandSlot.gameObject.SetActive(true);
            rightHandSlot.gameObject.SetActive(true);
            bothHandSlot.gameObject.SetActive(false);

            if (leftItem != null)
            {
                DraggableItem draggable = _draggableItemPool.Get();
                draggable.SetConfiguration(leftItem.Configuration);
                draggable.SetEntityId(leftEntityId);
                leftHandSlot.SetItem(draggable);
                Debug.Log("[InventoryUI] Refresh slot LeftHand");
            }

            if (rightItem != null)
            {
                DraggableItem draggable = _draggableItemPool.Get();
                draggable.SetConfiguration(rightItem.Configuration);
                draggable.SetEntityId(rightEntityId);
                rightHandSlot.SetItem(draggable);
                Debug.Log("[InventoryUI] Refresh slot RightHand");
            }
        }

        CloseCurrentActionMenu(true);
    }

    #region Pool Management

    private DraggableItem OnCreateDraggableItem()                   => Instantiate(draggableItemPrefab, poolContainer);
    private void          OnGetDraggableItem(DraggableItem item)    => item.gameObject.SetActive(true);
    private void          OnReleaseDraggableItem(DraggableItem item) { item.ResetDragState(); item.transform.parent = poolContainer; item.gameObject.SetActive(false); }

    #endregion
}
