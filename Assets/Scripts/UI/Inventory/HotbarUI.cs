using System.Collections.Generic;
using Mirror;
using Sim;
using Sim.Enums;
using Sim.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using Key = UnityEngine.InputSystem.Key;

/// <summary>
/// Barre d'accès rapide : miroir des premiers slots de la poche, toujours visible
/// (enfant de Default View). Molette / touches 1-N pour choisir l'item « en main ».
///
/// Modèle POINTEUR : l'item équipé NE QUITTE JAMAIS son slot de poche — la main du
/// personnage n'affiche qu'une projection (entité visuelle spawnée par le serveur,
/// bridge partagé avec la ligne DB de l'item). La sélection est 100 % cliente et
/// instantanée ; le serveur ne reçoit qu'un <see cref="C2S_EquipPocketSlot"/>
/// debouncé (aucune écriture DB → aucun conflit de version possible, quel que soit
/// le rythme de scroll).
/// </summary>
public class HotbarUI : MonoBehaviour
{
    [Header("Slots")]
    [SerializeField, Tooltip("Cellules pré-autorées dans le prefab (style Synty ActionBar). " +
        "Si non vide, slotTemplate/slotsContainer/slotCount sont ignorés.")]
    private List<HotbarSlotUI> prewiredSlots = new List<HotbarSlotUI>();
    [SerializeField, Tooltip("Fallback : template désactivé cloné quand aucune cellule pré-autorée.")]
    private HotbarSlotUI slotTemplate;
    [SerializeField] private Transform slotsContainer;
    [SerializeField, Tooltip("Nombre de slots poche exposés dans la barre (SlotIndex 0..N-1).")]
    private int slotCount = 6;

    [SerializeField, Tooltip("CanvasGroup de la barre — dimée quand le joueur porte un objet encombrant (non pocketable).")]
    private CanvasGroup barGroup;

    private readonly List<HotbarSlotUI> _slots = new List<HotbarSlotUI>();

    // Contenu poche par SlotIndex (reconstruit à chaque S2C_PocketSync).
    private readonly Dictionary<int, S2C_PocketItem> _pocketBySlot = new Dictionary<int, S2C_PocketItem>();

    private int _selectedIndex = -1;   // sélection locale ; -1 = mains libres

    // Envoi du pointeur : debouncé pour qu'un scroll rapide ne pointe que la sélection
    // finale (pas un spawn/despawn de projection par cran traversé).
    private const float EquipDebounce = 0.12f;
    private bool  _equipDirty;
    private float _equipSendAt;
    private float _lastSentAt;

    private BuildModeEnum _buildMode = BuildModeEnum.NONE;

    private void Awake()
    {
        EnsureSlots();
    }

    private void OnEnable()
    {
        ClientItemManager.PocketSync     += OnPocketSync;
        ClientItemManager.MoveItemResult += OnMoveItemResult;
        PlayerHands.OnHandChanged        += OnHandsChanged;
        BuildManager.OnModeChanged       += OnBuildModeChanged;

        if (ClientItemManager.LastPocketSnapshot.HasValue)
            OnPocketSync(ClientItemManager.LastPocketSnapshot.Value);
        else
            Refresh();
    }

    private void OnDisable()
    {
        ClientItemManager.PocketSync     -= OnPocketSync;
        ClientItemManager.MoveItemResult -= OnMoveItemResult;
        PlayerHands.OnHandChanged        -= OnHandsChanged;
        BuildManager.OnModeChanged       -= OnBuildModeChanged;
    }

    private void Update()
    {
        // L'envoi debouncé du pointeur part même si l'input vient d'être inhibé
        // (ex. le joueur ouvre le chat juste après un scroll).
        if (_equipDirty && Time.unscaledTime >= _equipSendAt) SendEquip();

        if (!InputAllowed()) return;

        // Key.Digit1..Digit9 sont contigus dans l'enum du nouveau Input System (rangée physique
        // des chiffres — indépendant AZERTY/QWERTY, pas besoin de Shift sur AZERTY).
        for (int i = 0; i < _slots.Count; i++)
            if (GameInput.Down((Key)((int)Key.Digit1 + i))) { Select(i); return; }

        // Molette : ignorée au-dessus de l'UI (scroll de listes : props, conteneurs…).
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f
            && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            int dir  = scroll < 0f ? 1 : -1;   // molette bas = slot suivant (convention classique)
            int from = _selectedIndex < 0 ? (dir > 0 ? -1 : 0) : _selectedIndex;
            int next = ((from + dir) % _slots.Count + _slots.Count) % _slots.Count;
            Select(next);
        }
    }

    private bool InputAllowed()
    {
        PlayerController local = PlayerController.Local;
        if (local == null) return false;
        if (local.IsDriving || local.IsPassenger || local.IsKnockedDown) return false;
        if (SubGameController.IsActive) return false;
        if (_buildMode != BuildModeEnum.NONE) return false;   // la molette appartient à la BuildCamera
        if (Sim.Utils.CursorLock.IsTypingInInputField()) return false;
        if (IsCarryingBulky()) return false;   // objet encombrant : barre inopérante
        return true;
    }

    /// <summary>Objet « encombrant » porté : non pocketable → portage physique réel, hors
    /// hotbar (colis, sac à dos en main…) — la barre est grisée et inopérante.</summary>
    private bool IsCarryingBulky()
    {
        ItemBehaviour held = PlayerController.Local != null && PlayerController.Local.PlayerHands != null
            ? PlayerController.Local.PlayerHands.RightHandItem : null;
        return held != null && held.Configuration != null && !held.Configuration.AllowedInPocket;
    }

    private void OnBuildModeChanged(BuildModeEnum mode) => _buildMode = mode;

    // ── Sélection (100 % cliente) ─────────────────────────────────────────────

    private void Select(int index)
    {
        if (index < 0 || index >= _slots.Count || index == _selectedIndex) return;
        _selectedIndex = index;
        Refresh();
        QueueEquip();
    }

    private void QueueEquip()
    {
        _equipDirty  = true;
        _equipSendAt = Time.unscaledTime + EquipDebounce;
    }

    private void SendEquip()
    {
        _equipDirty = false;
        if (!NetworkClient.isConnected) return;
        _lastSentAt = Time.unscaledTime;
        NetworkClient.Send(new C2S_EquipPocketSlot { SlotIndex = _selectedIndex });
    }

    /// <summary>
    /// Filet de convergence : si la main ne reflète plus le slot sélectionné (item
    /// consommé/déplacé, changement de room qui a déspawné la projection…), re-pointe.
    /// Jamais pendant un portage encombrant (la main est légitimement occupée).
    /// </summary>
    private void ReconcilePointer()
    {
        if (_equipDirty || Time.unscaledTime - _lastSentAt < 0.4f) return;
        if (IsCarryingBulky()) return;
        PlayerHands hands = PlayerController.Local != null ? PlayerController.Local.PlayerHands : null;
        if (hands == null) return;
        bool handHas = hands.RightHandItem != null;
        bool slotHas = _pocketBySlot.ContainsKey(_selectedIndex);
        if (handHas != slotHas) QueueEquip();
    }

    private void OnHandsChanged()
    {
        Refresh();
        ReconcilePointer();
    }

    // ── Drag & drop depuis l'inventaire ──────────────────────────────────────

    /// <summary>
    /// Drop d'un draggable d'inventaire (poche/conteneur) sur une cellule de la
    /// barre : route un move/swap vers le slot de poche correspondant — la cellule
    /// n'est qu'une vue des premiers slots de poche. Le serveur revalide, et les
    /// snapshots (PocketSync) réconcilient l'affichage.
    /// </summary>
    public void HandleDropOnSlot(HotbarSlotUI slot, DraggableItem dragged)
    {
        if (slot == null || dragged == null || dragged.ItemSlot == null) return;
        if (!NetworkClient.isConnected) return;
        string charId = PlayerController.Local?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId)) return;

        if (dragged.ItemConfig != null && !dragged.ItemConfig.AllowedInPocket) {
            WorldToastManager.ShowError(InventoryToasts.NotPocketable);
            return;
        }

        string pocketKey = $"pocket:{charId}";
        ItemSlot src = dragged.ItemSlot;
        if (string.IsNullOrEmpty(src.PlaceId)) return;
        int index = slot.SlotIndex;
        if (src.PlaceId == pocketKey && src.SlotIndex == index) return;   // même slot

        if (_pocketBySlot.TryGetValue(index, out S2C_PocketItem occupant)) {
            if (occupant.EntityId == dragged.EntityId) return;
            NetworkClient.Send(new C2S_SwapItems {
                EntityIdA  = dragged.EntityId,
                PlaceIdA   = src.PlaceId,
                SlotIndexA = src.SlotIndex,
                EntityIdB  = occupant.EntityId,
                PlaceIdB   = pocketKey,
                SlotIndexB = index,
            });
        } else {
            NetworkClient.Send(new C2S_MoveItem {
                EntityId    = dragged.EntityId,
                FromPlaceId = src.PlaceId,
                ToPlaceId   = pocketKey,
                ToSlotIndex = index,
            });
        }
    }

    // ── Événements serveur ────────────────────────────────────────────────────

    private void OnPocketSync(S2C_PocketSync msg)
    {
        _pocketBySlot.Clear();
        if (msg.Items != null)
            foreach (S2C_PocketItem item in msg.Items)
                _pocketBySlot[item.SlotIndex] = item;
        Refresh();
        ReconcilePointer();
    }

    private void OnMoveItemResult(S2C_MoveItemResult msg)
    {
        // Échec d'un move (drag vers la barre) : les snapshots resynchronisent.
        if (!msg.Success) Refresh();
    }

    // ── Affichage ─────────────────────────────────────────────────────────────

    private void EnsureSlots()
    {
        // Cellules pré-autorées dans le prefab (style Synty ActionBar) : on les adopte
        // telles quelles — index, touche et clic-sélection posés au runtime.
        if (_slots.Count == 0 && prewiredSlots != null && prewiredSlots.Count > 0) {
            foreach (HotbarSlotUI prewired in prewiredSlots) {
                if (prewired == null) continue;
                prewired.SlotIndex = _slots.Count;
                prewired.SetKey((_slots.Count + 1).ToString());
                BindClick(prewired);
                _slots.Add(prewired);
            }
            return;
        }

        if (slotTemplate == null || slotsContainer == null) return;
        while (_slots.Count < slotCount) {
            HotbarSlotUI slot = Instantiate(slotTemplate, slotsContainer);
            slot.name      = $"Hotbar Slot {_slots.Count}";
            slot.SlotIndex = _slots.Count;
            slot.SetKey((_slots.Count + 1).ToString());
            slot.gameObject.SetActive(true);
            BindClick(slot);
            _slots.Add(slot);
        }
    }

    /// <summary>Clic sur une cellule (Button du style Synty) = sélection du slot.</summary>
    private void BindClick(HotbarSlotUI slot)
    {
        UnityEngine.UI.Button btn = slot.GetComponent<UnityEngine.UI.Button>();
        if (btn == null) return;
        int idx = slot.SlotIndex;
        btn.onClick.AddListener(() => { if (InputAllowed()) Select(idx); });
    }

    private void Refresh()
    {
        EnsureSlots();

        // Modèle pointeur : l'item équipé reste DANS son slot (le snapshot poche fait
        // foi, pas besoin d'affichage « fantôme ») — la sélection n'est qu'un anneau.
        for (int i = 0; i < _slots.Count; i++) {
            Sprite icon = null;
            if (_pocketBySlot.TryGetValue(i, out S2C_PocketItem entry))
                icon = DatabaseManager.GetItemConfigById(entry.ConfigId)?.Icon;

            _slots[i].SetIcon(icon);
            _slots[i].SetSelected(i == _selectedIndex);
        }

        if (barGroup) barGroup.alpha = IsCarryingBulky() ? 0.35f : 1f;
    }
}
