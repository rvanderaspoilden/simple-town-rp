using System.Collections.Generic;
using Mirror;
using Sim;
using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Carte de détail de l'item sélectionné dans l'inventaire (clic gauche sur un slot).
/// Reprend le contenu de la tooltip (<see cref="DraggableItem.BuildDetailContent"/>) :
/// icône, nom, description, effets — plus une grille de capacité pour les
/// items-conteneurs et des actions contextuelles selon la place source de l'item.
/// Structure autorée dans le prefab HUD ; cellules et boutons clonés depuis des
/// templates désactivés.
/// </summary>
public class ItemDetailCardUI : MonoBehaviour
{
    [Header("Références (prefab)")]
    [SerializeField] private Image    iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text typeChipText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text effectsText;
    [SerializeField] private GameObject contentSection;
    [SerializeField] private Transform  slotsGrid;
    [SerializeField] private Image      slotCellTemplate;
    [SerializeField] private Transform  actionsContainer;
    [SerializeField] private Button     actionButtonTemplate;

    [Header("Couleurs cellules contenu")]
    [SerializeField] private Color cellEmptyColor    = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] private Color cellOccupiedColor = new Color(0.95f, 0.65f, 0.35f, 0.9f);

    private readonly List<Image>  _cells   = new List<Image>();
    private readonly List<Button> _buttons = new List<Button>();

    private int _entityId = -1;

    /// <summary>EntityId de l'item affiché (-1 = carte masquée). Permet à l'InventoryUI
    /// de re-résoudre la sélection après un refresh (snapshot, move, équipement).</summary>
    public int SelectedEntityId => _entityId;

    public void Clear()
    {
        _entityId = -1;
        gameObject.SetActive(false);
    }

    public void Show(DraggableItem item)
    {
        if (item == null) { Clear(); return; }
        item.BuildDetailContent(out Sprite icon, out string label, out string desc,
            out string effects, out string container);
        if (icon == null && string.IsNullOrEmpty(label)) { Clear(); return; }

        _entityId = item.EntityId;
        gameObject.SetActive(true);

        if (iconImage) { iconImage.sprite = icon; iconImage.enabled = icon != null; }
        if (nameText)  nameText.text = label ?? string.Empty;
        if (typeChipText) typeChipText.text = TypeLabel(item);

        if (descriptionText) {
            bool hasDesc = !string.IsNullOrEmpty(desc);
            descriptionText.gameObject.SetActive(hasDesc);
            if (hasDesc) descriptionText.text = desc;
        }
        if (effectsText) {
            bool hasFx = !string.IsNullOrEmpty(effects);
            effectsText.gameObject.SetActive(hasFx);
            if (hasFx) effectsText.text = effects;
        }

        BuildContentCells(item);
        BuildActions(item);
    }

    // ── Chip de catégorie ─────────────────────────────────────────────────────

    private static string TypeLabel(DraggableItem item)
    {
        if (item.IsPackedProp) return "MEUBLE";
        ItemConfig cfg = item.ItemConfig;
        if (cfg == null) return string.Empty;
        if (cfg is WeaponConfig) return "ARME";
        if (cfg is AmmoConfig)   return "MUNITIONS";
        var cc = ItemContainerConfig.Of(cfg);
        if (cc != null && cc.IsContainer) return cc.Wearable ? "SAC" : "CONTENEUR";
        switch (cfg.Type) {
            case ItemType.CONSUMABLE: return "CONSO";
            case ItemType.PACKAGE:    return "COLIS";
            case ItemType.WASTE:      return "DÉCHET";
            default:                  return "DIVERS";
        }
    }

    // ── Grille de capacité (items-conteneurs) ─────────────────────────────────

    /// <summary>Affiche SlotCount cellules ; les occupées sont teintées quand le panneau
    /// de CE conteneur est ouvert (seule source client de son contenu), sinon tout vide.</summary>
    private void BuildContentCells(DraggableItem item)
    {
        ContainerConfig cc = item.ItemConfig != null ? ItemContainerConfig.Of(item.ItemConfig) : null;
        bool isContainer = cc != null && cc.IsContainer;
        if (contentSection) contentSection.SetActive(isContainer);
        if (!isContainer || slotCellTemplate == null || slotsGrid == null) return;

        int occupied = 0;
        var panel = ContainerPanelUI.Item;
        if (panel != null && panel.IsShowingItemContainer(item.EntityId)) {
            int free = panel.FreeSlotCount();
            if (free >= 0) occupied = Mathf.Max(0, cc.SlotCount - free);
        }

        while (_cells.Count < cc.SlotCount) {
            Image cell = Instantiate(slotCellTemplate, slotsGrid);
            cell.gameObject.SetActive(true);
            _cells.Add(cell);
        }
        for (int i = 0; i < _cells.Count; i++) {
            bool visible = i < cc.SlotCount;
            _cells[i].gameObject.SetActive(visible);
            if (visible) _cells[i].color = i < occupied ? cellOccupiedColor : cellEmptyColor;
        }
    }

    // ── Actions contextuelles ─────────────────────────────────────────────────

    private void BuildActions(DraggableItem item)
    {
        foreach (Button b in _buttons) if (b) Destroy(b.gameObject);
        _buttons.Clear();
        if (actionsContainer == null || actionButtonTemplate == null) return;

        string charId = PlayerController.Local?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId) || item.ItemSlot == null) return;
        string src = item.ItemSlot.PlaceId ?? string.Empty;
        int entityId = item.EntityId;
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;

        if (src.StartsWith("pocket:")) {
            // Pas de « Tenir en main » : le slot actif de la hotbar est la seule
            // autorité sur l'item porté.
            AddAction("Lâcher", () => { if (NetworkClient.isConnected) NetworkClient.Send(new C2S_DropFromInventory { EntityId = entityId }); });
        }
        else if (src.StartsWith("hand_right:")) {
            // Vue main indicative — seule exception : porter un sac (c'est de
            // l'équipement, pas un choix d'item actif). Lâcher un objet encombrant
            // passe par les actions monde.
            if (ItemContainerConfig.Of(item.ItemConfig)?.Wearable == true)
                AddAction("Porter sur le dos", () => SendMove(entityId, src, $"backpack:{charId}", 0));
        }
        else if (src.StartsWith("backpack:")) {
            var hands = PlayerController.Local != null ? PlayerController.Local.PlayerHands : null;
            if (hands != null && hands.RightHandItem == null)
                AddAction("Retirer", () => SendMove(entityId, src, $"hand_right:{charId}", 0));
        }
        else if (inv != null) {
            // Item d'un conteneur ouvert (colis, sac, meuble) : « Prendre » vers
            // l'inventaire — même cible que le quick-move double-clic.
            var draggable = item;
            AddAction("Prendre", () => {
                if (inv.TryFindQuickMoveTargetForItem(draggable, out string placeKey, out int slotIndex))
                    SendMove(draggable.EntityId, src, placeKey, slotIndex);
                else
                    WorldToastManager.ShowError(InventoryToasts.NoSpaceInInventory);
            });
        }
    }

    private void AddAction(string label, System.Action onClick)
    {
        Button b = Instantiate(actionButtonTemplate, actionsContainer);
        b.gameObject.SetActive(true);
        TMP_Text txt = b.GetComponentInChildren<TMP_Text>(true);
        if (txt) txt.text = label;
        b.onClick.AddListener(() => onClick?.Invoke());
        _buttons.Add(b);
    }

    private static void SendMove(int entityId, string from, string to, int toSlot)
    {
        if (!NetworkClient.isConnected || entityId <= 0) return;
        NetworkClient.Send(new C2S_MoveItem {
            EntityId = entityId, FromPlaceId = from, ToPlaceId = to, ToSlotIndex = toSlot,
        });
    }
}
