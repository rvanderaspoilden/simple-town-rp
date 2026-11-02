using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    [Header("Identité (nom + argent)")]
    [SerializeField, Tooltip("Nom du personnage, affiché en tête de l'inventaire (ex-Character Information).")]
    private TMPro.TextMeshProUGUI nameText;
    [SerializeField, Tooltip("Argent détenu, affiché en tête de l'inventaire (ex-Character Information).")]
    private TMPro.TextMeshProUGUI moneyText;

    [Header("Settings")]
    [SerializeField, Tooltip("Slot unique « en main » (item actif, 1H ou 2H).")]
    private ItemSlot rightHandSlot;
    [SerializeField, Tooltip("Slot équipement « sac à dos » (conteneur wearable porté).")]
    private ItemSlot backpackSlot;
    [SerializeField, Tooltip("Carte de détail de l'item sélectionné (panneau de droite).")]
    private ItemDetailCardUI detailCard;
    [Header("Poche (grille)")]
    [SerializeField, Tooltip("Template désactivé, cloné pour chaque slot de poche (SlotCount serveur).")]
    private ItemSlot pocketSlotTemplate;
    [SerializeField, Tooltip("Parent (GridLayoutGroup) recevant les slots de poche clonés.")]
    private Transform pocketGrid;

    // Slots poche clonés depuis le template — indexés par SlotIndex.
    private readonly List<ItemSlot> _pocketSlots = new List<ItemSlot>();

    // Slots affichés avant le premier S2C_PocketSync (le snapshot serveur fait autorité ensuite).
    private const int DefaultPocketSlotCount = 8;

    [SerializeField] private Transform     poolContainer;
    [SerializeField] private DraggableItem draggableItemPrefab;

    // Menu contextuel partagé unique (InventoryActionMenu.Shared) — plus de menu gauche/droite.
    [Header("Only for debug")]
    [SerializeField] private InventoryActionMenu currentActionMenu;
    private ItemSlot _menuSlot;            // slot pour lequel le menu est ouvert (toggle)
    private Sim.Interactables.Action _protoDrop;

    private GenericPool<DraggableItem> _draggableItemPool;

    private void Awake()
    {
        EnsurePool();

        // Vue main purement indicative : ni drag, ni drop, ni swap — le slot actif de
        // la hotbar est la seule autorité sur l'item porté. Les poches gardent le swap
        // visuel (pocket↔pocket, pocket↔container).
        if (rightHandSlot) rightHandSlot.AcceptsDrops = false;
        EnsurePocketSlots(DefaultPocketSlotCount);
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

    /// <summary>
    /// Clone le template jusqu'à disposer de <paramref name="count"/> slots de poche.
    /// Idempotent — appelé à l'Awake (valeur par défaut) puis à chaque snapshot
    /// (SlotCount serveur = PocketCapacity, autoritaire). Un excédent est désactivé.
    /// </summary>
    private void EnsurePocketSlots(int count)
    {
        if (pocketSlotTemplate == null || pocketGrid == null) return;
        while (_pocketSlots.Count < count) {
            ItemSlot slot = Instantiate(pocketSlotTemplate, pocketGrid);
            slot.name      = $"Pocket Slot {_pocketSlots.Count}";
            slot.SlotIndex = _pocketSlots.Count;
            slot.CanSwap   = true;
            slot.gameObject.SetActive(true);
            _pocketSlots.Add(slot);
        }
        for (int i = 0; i < _pocketSlots.Count; i++)
            if (_pocketSlots[i]) _pocketSlots[i].gameObject.SetActive(i < count);

        string charId = PlayerController.Local != null ? PlayerController.Local.CharacterData?.Id : null;
        if (!string.IsNullOrEmpty(charId))
            foreach (ItemSlot s in _pocketSlots) if (s) s.PlaceId = $"pocket:{charId}";
    }

    private bool IsPocketSlot(ItemSlot slot) => slot != null && _pocketSlots.Contains(slot);

    /// <summary>Premier slot de poche libre (grille) — utilisé par « Ranger » de la carte de détail.</summary>
    public bool TryFindFreePocketSlot(out int slotIndex)
    {
        slotIndex = -1;
        foreach (ItemSlot s in _pocketSlots)
            if (s != null && s.gameObject.activeSelf && s.Item == null) { slotIndex = s.SlotIndex; return true; }
        return false;
    }

    // ── Carte de détail (sélection) ─────────────────────────────────────────

    /// <summary>Re-résout la sélection de la carte après un refresh : garde l'item
    /// sélectionné s'il est toujours dans nos slots, sinon retombe sur le premier
    /// item de l'inventaire (poche → main → sac), sinon masque la carte.</summary>
    private void RefreshDetailCard()
    {
        if (detailCard == null || !gameObject.activeInHierarchy) return;
        DraggableItem current = FindDraggableByEntity(detailCard.SelectedEntityId);
        if (current == null) current = FindFirstInventoryItem();
        if (current != null) detailCard.Show(current);
        else detailCard.Clear();
    }

    private DraggableItem FindDraggableByEntity(int entityId)
    {
        if (entityId <= 0) return null;
        foreach (ItemSlot s in _pocketSlots)
            if (s != null && s.Item != null && s.Item.EntityId == entityId) return s.Item;
        if (rightHandSlot != null && rightHandSlot.Item != null && rightHandSlot.Item.EntityId == entityId) return rightHandSlot.Item;
        if (backpackSlot  != null && backpackSlot.Item  != null && backpackSlot.Item.EntityId  == entityId) return backpackSlot.Item;
        return null;
    }

    private DraggableItem FindFirstInventoryItem()
    {
        foreach (ItemSlot s in _pocketSlots)
            if (s != null && s.gameObject.activeSelf && s.Item != null) return s.Item;
        if (rightHandSlot != null && rightHandSlot.Item != null) return rightHandSlot.Item;
        if (backpackSlot  != null && backpackSlot.Item  != null) return backpackSlot.Item;
        return null;
    }

    private void OnEnable()
    {
        // Sélection par défaut à l'ouverture : premier item de l'inventaire
        // (RefreshDetailCard choisit après le refresh des slots).
        if (detailCard) detailCard.Clear();
        UpdateUI();
        // Identité (nom + argent) : rafraîchie à chaque ouverture depuis la source
        // autoritaire (le panneau est inactif au démarrage, donc pas de singleton
        // persistant) + live tant qu'il est ouvert.
        RefreshIdentity();
        PlayerBankAccount.OnLocalMoneyChanged += OnLocalMoneyChanged;
        PlayerController.OnCharacterDataChanged += OnCharacterDataChanged;
        // Reset défensif : si l'inventaire a été désactivé pendant un drag, les slots
        // peuvent rester dimés (OnDragEnded fire après OnDisable → handler déjà désinscrit).
        ResetDropAcceptance();

        DraggableItem.OnLeftClick   += OnItemLeftClicked;
        DraggableItem.OnRightClick  += OnItemRightClicked;
        DraggableItem.OnDoubleClick += OnItemDoubleClicked;
        DraggableItem.OnStartDrag   += OnItemStartDrag;
        DraggableItem.OnDragEnded   += OnItemDragEnded;
        ItemSlot.OnItemMove         += OnItemMoved;
        ItemSlot.OnItemSwap         += OnItemsSwapped;
        PlayerHands.OnHandChanged   += OnPlayerHandChanged;
        ClientItemManager.MoveItemResult += OnMoveItemResultReceived;
        ClientItemManager.PocketSync     += OnPocketSyncReceived;

        // Replay du dernier snapshot poche : la session est ouverte serveur-side au
        // connect ; les opens/closes de la HUD ne re-déclenchent pas de S2C_PocketSync.
        if (ClientItemManager.LastPocketSnapshot.HasValue)
            ApplyPocketSnapshot(ClientItemManager.LastPocketSnapshot.Value);

        // Ouvrir l'inventaire ré-affiche aussi la grille du colis s'il est tenu.
        ContainerPanelUI.Item?.RefreshHeldContainer();
    }

    private void OnDisable()
    {
        // Release par slot — NE PAS pool.Dispose() car le pool est aussi consommé par
        // ContainerPanelUI, et un Dispose() global libérerait ses draggables aussi.
        ReleaseSlot(rightHandSlot);
        ReleaseSlot(backpackSlot);

        PlayerBankAccount.OnLocalMoneyChanged -= OnLocalMoneyChanged;
        PlayerController.OnCharacterDataChanged -= OnCharacterDataChanged;

        DraggableItem.OnLeftClick   -= OnItemLeftClicked;
        DraggableItem.OnRightClick  -= OnItemRightClicked;
        DraggableItem.OnDoubleClick -= OnItemDoubleClicked;
        DraggableItem.OnStartDrag   -= OnItemStartDrag;
        DraggableItem.OnDragEnded   -= OnItemDragEnded;
        ItemSlot.OnItemMove         -= OnItemMoved;
        ItemSlot.OnItemSwap         -= OnItemsSwapped;
        PlayerHands.OnHandChanged   -= OnPlayerHandChanged;
        ClientItemManager.MoveItemResult -= OnMoveItemResultReceived;
        ClientItemManager.PocketSync     -= OnPocketSyncReceived;

        // Ferme les deux conteneurs avec l'inventaire (meuble + colis), comme un
        // clic « X » ou Escape. Le colis ré-apparaîtra à la prochaine ouverture
        // d'inventaire (RefreshHeldContainer) tant qu'il est tenu.
        ContainerPanelUI.Prop?.Close();
        ContainerPanelUI.Item?.Close();
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

        // Inventaire classique (migration hotbar) : on ne ferme PLUS l'inventaire quand le
        // joueur bouge. En TPS, tpsCurrentSpeed est une vitesse amortie — l'ancienne règle
        // « HUD fermé pendant le mouvement » refermait le panneau dans la frame d'ouverture
        // tant qu'elle n'était pas retombée à zéro (toggle I inutilisable en marchant).
    }

    // ── Identité (nom + argent) ───────────────────────────────────────────────

    private void RefreshIdentity()
    {
        PlayerController local = PlayerController.Local;
        if (local == null) return;
        if (nameText != null && local.CharacterData != null)
            nameText.text = local.CharacterData.Identity.FullName;
        if (moneyText != null && local.PlayerBankAccount != null)
            moneyText.text = local.PlayerBankAccount.Money.ToString();
    }

    private void OnLocalMoneyChanged(int amount)
    {
        if (moneyText != null) moneyText.text = amount.ToString();
    }

    private void OnCharacterDataChanged(Sim.Entities.CharacterData data)
    {
        if (nameText != null && data != null) nameText.text = data.Identity.FullName;
    }

    private void OnPlayerHandChanged()
    {
        Debug.Log("[InventoryUI] [OnPlayerHandChanged] Refreshing all slots");
        CloseCurrentActionMenu(true);
        UpdateUI();
    }

    private void OnItemLeftClicked(DraggableItem draggableItem)
    {
        CloseCurrentActionMenu();
        // Clic gauche = sélection pour la carte de détail (slots inventaire ET conteneurs).
        if (detailCard && draggableItem != null) detailCard.Show(draggableItem);
    }
    private void OnItemStartDrag(DraggableItem draggableItem) {
        CloseCurrentActionMenu();
        ApplyDropAcceptanceForDrag(draggableItem);
    }
    private void OnItemDragEnded(DraggableItem draggableItem)     => ResetDropAcceptance(draggableItem);

    /// <summary>
    /// Désactive visuellement (alpha + raycasts) les slots inventaire qui ne peuvent pas
    /// accueillir <paramref name="dragged"/> :
    ///  - poches : refusé si !ItemConfig.AllowedInPocket ;
    ///  - slot main : jamais (vue indicative — l'item actif se choisit via la hotbar).
    /// Le slot d'origine reste toujours accepté (annulation propre). Le serveur revalide
    /// de toute façon (HandPlaceContext / PocketPlaceContext.ValidateAsTarget).
    /// </summary>
    private void ApplyDropAcceptanceForDrag(DraggableItem dragged) {
        if (dragged == null || dragged.ItemConfig == null) return;

        bool allowedInPocket = dragged.ItemConfig.AllowedInPocket;

        if (rightHandSlot) rightHandSlot.SetDropAcceptance(false);
        bool isWearable = Sim.Scriptables.ItemContainerConfig.Of(dragged.ItemConfig)?.Wearable == true;
        if (backpackSlot) backpackSlot.SetDropAcceptance(isWearable);
        foreach (ItemSlot s in _pocketSlots) if (s) s.SetDropAcceptance(allowedInPocket);

        // L'origine reste toujours active : permet une annulation immédiate sans devoir
        // sortir le pointeur de la zone, et évite que le drop sur soi-même soit refusé.
        if (dragged.ItemSlot != null) dragged.ItemSlot.SetDropAcceptance(true);
    }

    private void ResetDropAcceptance(DraggableItem dragged = null) {
        if (rightHandSlot) rightHandSlot.SetDropAcceptance(true);
        if (backpackSlot)  backpackSlot.SetDropAcceptance(true);
        foreach (ItemSlot s in _pocketSlots) if (s) s.SetDropAcceptance(true);
    }

    /// <summary>
    /// Double-clic gauche : quick-move main/poche → conteneur ouvert. Le sens conteneur
    /// → inventaire est géré par <see cref="ContainerPanelUI.OnItemDoubleClicked"/>.
    /// </summary>
    private void OnItemDoubleClicked(DraggableItem item)
    {
        if (item == null || item.ItemSlot == null) return;
        var src = item.ItemSlot;
        bool srcIsOurs = src == backpackSlot || IsPocketSlot(src);
        if (!srcIsOurs) return;

        var container = ContainerPanelUI.QuickMoveTarget();
        if (container == null || !container.IsOpen) return;     // pas de conteneur ouvert → no-op

        // Priorité : remplir une pile existante mergeable (règle utilisateur) avant
        // de chercher un slot vide. Pour un item non empilable FindMergeableSlotFor
        // renvoie -1 → fallback immédiat sur FindFirstFreeSlot.
        int targetSlot = container.FindMergeableSlotFor(item.ItemConfig);
        if (targetSlot < 0) targetSlot = container.FindFirstFreeSlot();
        if (targetSlot < 0) {
            WorldToastManager.ShowError(InventoryToasts.ContainerFull);
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
    /// Choisit une destination « main/poche » libre pour un quick-move depuis un conteneur.
    /// Priorité : premier slot de poche libre (item pocketable) ; la main ne sert qu'aux
    /// objets encombrants — même politique que le ramassage serveur. Retourne le placeKey
    /// wire ("pocket:{charId}" / "hand_right:{charId}") + slotIndex, ou false sinon.
    /// </summary>
    public bool TryFindQuickMoveTargetForItem(DraggableItem item, out string placeKey, out int slotIndex)
    {
        placeKey = null; slotIndex = 0;
        if (PlayerController.Local == null) return false;
        string charId = PlayerController.Local.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) return false;

        var hands = PlayerController.Local.PlayerHands;
        ItemConfig cfg = item != null ? item.ItemConfig : null;

        // Item pocketable → premier slot de poche libre (sinon false → toast
        // « Pas de place dans l'inventaire » plutôt qu'un MoveItem refusé serveur).
        bool allowedInPocket = cfg != null && cfg.AllowedInPocket;
        if (allowedInPocket) {
            foreach (ItemSlot s in _pocketSlots) {
                if (s != null && s.gameObject.activeSelf && s.Item == null) {
                    placeKey = $"pocket:{charId}"; slotIndex = s.SlotIndex; return true;
                }
            }
            return false;
        }

        // Objet encombrant (non pocketable) : en main si elle est libre.
        if (hands.RightHandItem == null) { placeKey = $"hand_right:{charId}"; return true; }
        return false;
    }

    private void OnItemRightClicked(DraggableItem draggableItem)
    {
        ItemSlot slot = draggableItem.ItemSlot;
        if (slot == null) return;

        // Re-clic sur le même slot → toggle fermeture.
        if (currentActionMenu != null && _menuSlot == slot) { CloseCurrentActionMenu(); return; }

        // Vue main indicative : pas de menu contextuel sur le slot main.
        if (IsPocketSlot(slot)) {
            // Item en poche → action « Lâcher » (au sol).
            DisplayPocketDropMenu(draggableItem); _menuSlot = slot;
        }
    }

    private void DisplayPocketDropMenu(DraggableItem item)
    {
        CloseCurrentActionMenu();
        var menu = InventoryActionMenu.Shared;
        if (menu == null) return;
        if (_protoDrop == null) _protoDrop = Resources.Load<Sim.Interactables.Action>("Configurations/Actions/DROP");
        if (_protoDrop == null) return;
        int eid = item.EntityId;
        menu.ShowSingleAction(_protoDrop, () => {
            if (NetworkClient.isConnected) NetworkClient.Send(new C2S_DropFromInventory { EntityId = eid });
        });
        currentActionMenu = menu;
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
    /// Swap visuel déjà appliqué dans ItemSlot.OnDrop, routé via C2S_SwapItems
    /// (un seul aller-retour serveur, deux PATCH atomiques).
    /// Sur échec serveur le snapshot S2C_ContainerOpened re-poussé reconciliera le panneau,
    /// et OnMoveItemResultReceived déclenchera UpdateUI() pour les mains.
    /// </summary>
    private void OnItemsSwapped(ItemSlot slotA, DraggableItem itemA, ItemSlot slotB, DraggableItem itemB)
    {
        if (string.IsNullOrEmpty(slotA.PlaceId) || string.IsNullOrEmpty(slotB.PlaceId)) return;
        // Belt-and-suspenders : un swap A==B (même place + même slotIndex) ne peut
        // qu'échouer en backend (409 conflit). ItemSlot.OnDrop a déjà un garde
        // same-slot, mais on protège ici au cas où un futur path déclencherait
        // OnItemSwap avec slotA == slotB.
        if (slotA.PlaceId == slotB.PlaceId && slotA.SlotIndex == slotB.SlotIndex) {
            Debug.LogWarning("[InventoryUI] OnItemsSwapped : same place+slot, skip C2S_SwapItems");
            return;
        }
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
        EnsurePocketSlots(msg.SlotCount > 0 ? msg.SlotCount : DefaultPocketSlotCount);
        foreach (ItemSlot s in _pocketSlots) ReleasePocketSlot(s);

        if (msg.Items == null) { RefreshDetailCard(); return; }
        foreach (var entry in msg.Items) {
            ItemSlot target = entry.SlotIndex >= 0 && entry.SlotIndex < _pocketSlots.Count
                            ? _pocketSlots[entry.SlotIndex] : null;
            if (target == null) {
                Debug.LogWarning($"[InventoryUI] Pocket entry slotIndex={entry.SlotIndex} hors range (0-{_pocketSlots.Count - 1}) ou slot UI non câblé, ignoré.");
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
            d.SetQuantity(Mathf.Max(1, entry.Quantity)); // poche = 1 item/slot, kept pour symétrie wire
            d.SetLoadedAmmo(entry.LoadedAmmo);           // arme : chargeur affiché dans la tooltip
            d.SetCashAmount(entry.CashAmount);           // liasse : montant affiché dans la tooltip
            target.SetItem(d);
        }

        RefreshDetailCard();
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
        if (!string.IsNullOrEmpty(msg.ErrorMessage)) WorldToastManager.ShowError(msg.ErrorMessage);
        UpdateUI();
    }

    public void CloseCurrentActionMenu(bool instantly = false)
    {
        if (!currentActionMenu) return;
        currentActionMenu.Hide(instantly);
        currentActionMenu = null;
        _menuSlot = null;
    }

    public void UpdateUI()
    {
        // Release uniquement les slots main/dos — pas pool.Dispose() global, qui libérerait
        // aussi les draggables hébergés ailleurs (ex. ContainerPanelUI).
        ReleaseSlot(rightHandSlot);
        ReleaseSlot(backpackSlot);

        if (PlayerController.Local == null) return;

        // Pose les PlaceId dès qu'on connaît le character id : permet à OnItemMoved
        // de router un drag vers/depuis ces slots via C2S_MoveItem.
        string charId = PlayerController.Local.CharacterData?.Id;
        if (!string.IsNullOrEmpty(charId)) {
            if (rightHandSlot) rightHandSlot.PlaceId = $"hand_right:{charId}";
            if (backpackSlot)  backpackSlot.PlaceId  = $"backpack:{charId}";
            foreach (ItemSlot s in _pocketSlots) if (s) s.PlaceId = $"pocket:{charId}";
        }

        var hands = PlayerController.Local.PlayerHands;
        ItemBehaviour heldItem = hands.RightHandItem;
        if (heldItem != null && rightHandSlot != null)
        {
            DraggableItem draggable = _draggableItemPool.Get();
            draggable.SetConfiguration(heldItem.Configuration);
            draggable.SetEntityId(hands.RightEntityId);
            draggable.SetDraggable(false);   // vue indicative : tooltip/sélection OK, pas de drag
            rightHandSlot.SetItem(draggable);
        }

        ItemBehaviour wornItem = hands.BackItem;
        if (wornItem != null && backpackSlot != null)
        {
            DraggableItem wornDraggable = _draggableItemPool.Get();
            wornDraggable.SetConfiguration(wornItem.Configuration);
            wornDraggable.SetEntityId(hands.BackEntityId);
            backpackSlot.SetItem(wornDraggable);
        }

        CloseCurrentActionMenu(true);
        RefreshDetailCard();
    }

    #region Pool Management

    private DraggableItem OnCreateDraggableItem()                   => Instantiate(draggableItemPrefab, poolContainer);
    private void          OnGetDraggableItem(DraggableItem item)    => item.gameObject.SetActive(true);
    private void          OnReleaseDraggableItem(DraggableItem item) { item.ResetDragState(); item.transform.SetParent(poolContainer, false); item.gameObject.SetActive(false); }

    #endregion
}
