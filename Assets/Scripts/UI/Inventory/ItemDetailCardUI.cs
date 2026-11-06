using System.Collections.Generic;
using Mirror;
using Sim;
using Sim.Scriptables;
using Sim.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
    [Header("Attributs (lignes de stats)")]
    [SerializeField] private GameObject statsSection;
    [SerializeField] private Transform  statsContainer;
    [SerializeField] private GameObject statRowTemplate;
    [SerializeField] private Sprite statIconDefault, statIconAmmo, statIconFood, statIconDrink, statIconMoney, statIconHealth;
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
    private readonly List<GameObject> _statRows = new List<GameObject>();
    // Actions associées à un raccourci clavier (affiché en pastille + déclenché dans Update).
    private readonly List<(Key key, System.Action act)> _keyActions = new List<(Key, System.Action)>();

    private int _entityId = -1;

    /// <summary>EntityId de l'item affiché (-1 = carte masquée). Permet à l'InventoryUI
    /// de re-résoudre la sélection après un refresh (snapshot, move, équipement).</summary>
    public int SelectedEntityId => _entityId;

    /// <summary>Émis quand l'item sélectionné change (-1 = plus de sélection). Les
    /// conteneurs de slots (inventaire, conteneur ouvert) s'y abonnent pour afficher
    /// l'outline autour du slot sélectionné.</summary>
    public static event System.Action<int> OnSelectionChanged;

    /// <summary>Dernier item sélectionné (-1 = aucun). Lu par les conteneurs pour re-poser
    /// l'outline après un re-peuplement de slots (hors event).</summary>
    public static int CurrentSelection { get; private set; } = -1;

    public void Clear()
    {
        if (_entityId != -1) { _entityId = -1; CurrentSelection = -1; OnSelectionChanged?.Invoke(-1); }
        gameObject.SetActive(false);
    }

    public void Show(DraggableItem item)
    {
        if (item == null) { Clear(); return; }
        item.BuildDetailContent(out Sprite icon, out string label, out string desc,
            out string effects, out string container);
        if (icon == null && string.IsNullOrEmpty(label)) { Clear(); return; }

        bool selectionChanged = _entityId != item.EntityId;
        _entityId = item.EntityId;
        CurrentSelection = _entityId;
        gameObject.SetActive(true);
        if (selectionChanged) OnSelectionChanged?.Invoke(_entityId);

        if (iconImage) { iconImage.sprite = icon; iconImage.enabled = icon != null; }
        if (nameText)  nameText.text = label ?? string.Empty;
        if (typeChipText) typeChipText.text = TypeLabel(item);

        if (descriptionText) {
            bool hasDesc = !string.IsNullOrEmpty(desc);
            descriptionText.gameObject.SetActive(hasDesc);
            if (hasDesc) descriptionText.text = desc;
        }
        // L'ancien bloc d'effets (un seul TMP multi-lignes) est remplacé par des lignes
        // de stats séparées (icône + label + valeur). On garde le TMP masqué pour
        // compat prefab.
        if (effectsText) effectsText.gameObject.SetActive(false);
        BuildStatRows(effects);

        BuildContentCells(item);
        BuildActions(item);
    }

    // ── Chip de catégorie ─────────────────────────────────────────────────────

    private static string TypeLabel(DraggableItem item)
    {
        if (item.IsPackedProp) return "MEUBLE";
        ItemConfig cfg = item.ItemConfig;
        if (cfg == null) return string.Empty;
        if (cfg is WeaponConfig || cfg is MeleeWeaponConfig) return "ARME";
        if (cfg is AmmoConfig)   return "MUNITIONS";
        if (cfg is ClothingConfig clothing) return clothing.Gender == Gender.FEMALE ? "VÊTEMENT FEMME" : "VÊTEMENT HOMME";
        var cc = ItemContainerConfig.Of(cfg);
        if (cc != null && cc.IsContainer) return cc.Wearable ? "SAC" : "CONTENEUR";
        switch (cfg.Type) {
            case ItemType.CONSUMABLE: return "CONSO";
            case ItemType.PACKAGE:    return "COLIS";
            case ItemType.WASTE:      return "DÉCHET";
            case ItemType.CLOTHING:   return "VÊTEMENT";
            default:                  return "DIVERS";
        }
    }

    // ── Attributs (lignes de stats) ───────────────────────────────────────────

    /// <summary>Transforme la chaîne d'effets multi-lignes en lignes de stats séparées
    /// (icône + label à gauche + valeur colorée à droite), comme la maquette.</summary>
    private void BuildStatRows(string effects)
    {
        foreach (GameObject r in _statRows) if (r) Destroy(r);
        _statRows.Clear();
        bool has = !string.IsNullOrEmpty(effects);
        if (statsSection != null) statsSection.SetActive(has);
        if (!has || statRowTemplate == null || statsContainer == null) return;

        foreach (string raw in effects.Replace("\r", "").Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            ParseStatLine(line, out string hex, out string label, out string value);
            // Valeur "métrique" (courte, type 10/30, +5, 67%) → label gauche / valeur droite.
            // Valeur descriptive (longue, ex. "moyennes (fusil d'assaut)") → tout dans le label.
            bool metric = value.Length > 0 && value.Length <= 10 && value.IndexOf('(') < 0;
            if (!metric) { label = string.IsNullOrEmpty(value) ? label : label + " : " + value; value = string.Empty; }
            GameObject row = Instantiate(statRowTemplate, statsContainer);
            row.SetActive(true);
            Transform iconTr = row.transform.Find("Icon");
            if (iconTr != null) { Image img = iconTr.GetComponent<Image>(); if (img != null) { img.sprite = PickIcon(label); img.enabled = img.sprite != null; } }
            Transform labelTr = row.transform.Find("Label");
            if (labelTr != null) { TMP_Text t = labelTr.GetComponent<TMP_Text>(); if (t != null) t.text = label; }
            Transform valueTr = row.transform.Find("Value");
            if (valueTr != null) { TMP_Text t = valueTr.GetComponent<TMP_Text>(); if (t != null) t.text = string.IsNullOrEmpty(hex) ? value : $"<color={hex}>{value}</color>"; }
            _statRows.Add(row);
        }
    }

    /// <summary>Décompose une ligne "[&lt;color=#x&gt;]Label( : | espace)valeur[&lt;/color&gt;]".</summary>
    private static void ParseStatLine(string line, out string hex, out string label, out string value)
    {
        hex = null; label = line; value = string.Empty;
        if (line.StartsWith("<color=")) {
            int gt = line.IndexOf('>');
            int close = line.LastIndexOf("</color>");
            if (gt > 0 && close > gt) { hex = line.Substring(7, gt - 7); line = line.Substring(gt + 1, close - gt - 1); }
        }
        int sep = line.IndexOf(" : ");
        if (sep >= 0) { label = line.Substring(0, sep).Trim(); value = line.Substring(sep + 3).Trim(); return; }
        int sp = line.LastIndexOf(' ');
        if (sp > 0) { label = line.Substring(0, sp).Trim(); value = line.Substring(sp + 1).Trim(); }
        else label = line.Trim();
    }

    private Sprite PickIcon(string label)
    {
        string l = label != null ? label.ToLowerInvariant() : string.Empty;
        if (l.Contains("chargeur") || l.Contains("muni"))                             return statIconAmmo   != null ? statIconAmmo   : statIconDefault;
        if (l.Contains("faim"))                                                        return statIconFood   != null ? statIconFood   : statIconDefault;
        if (l.Contains("soif"))                                                        return statIconDrink  != null ? statIconDrink  : statIconDefault;
        if (l.Contains("valeur") || l.Contains("réserv"))                              return statIconMoney  != null ? statIconMoney  : statIconDefault;
        if (l.Contains("vie") || l.Contains("soin") || l.Contains("pv") || l.Contains("santé")) return statIconHealth != null ? statIconHealth : statIconDefault;
        return statIconDefault;
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

    /// <summary>Déclenche l'action liée à une touche quand la carte est visible
    /// (hors saisie de texte). Les raccourcis sont posés par <see cref="BuildActions"/>.</summary>
    private void Update()
    {
        if (_keyActions.Count == 0 || !gameObject.activeInHierarchy) return;
        if (CursorLock.IsTypingInInputField()) return;
        for (int i = 0; i < _keyActions.Count; i++)
            if (GameInput.Down(_keyActions[i].key)) { _keyActions[i].act?.Invoke(); break; }
    }

    private void BuildActions(DraggableItem item)
    {
        foreach (Button b in _buttons) if (b) Destroy(b.gameObject);
        _buttons.Clear();
        _keyActions.Clear();
        if (actionsContainer == null || actionButtonTemplate == null) return;

        string charId = PlayerController.Local?.CharacterData?.Id;
        if (string.IsNullOrEmpty(charId) || item.ItemSlot == null) return;
        string src = item.ItemSlot.PlaceId ?? string.Empty;
        int entityId = item.EntityId;
        var inv = HUDManager.Instance != null ? HUDManager.Instance.InventoryUI : null;

        if (src.StartsWith("pocket:")) {
            // Arme (tenue OU rangée) : Recharger / Vider le chargeur.
            AddWeaponActions(item, src);
            // Pas de « Tenir en main » : le slot actif de la hotbar est la seule
            // autorité sur l'item porté.
            AddAction("Lâcher", () => { if (NetworkClient.isConnected) NetworkClient.Send(new C2S_DropFromInventory { EntityId = entityId }); }, Key.G);
            // Liasse : fusionner TOUTES les liasses du joueur dans celle-ci (les montants
            // s'additionnent, les autres items cash disparaissent). Visible seulement s'il
            // y a au moins une autre liasse à absorber.
            if (item.ItemConfig != null && item.ItemConfig.ID == CashItem.ConfigId && CountLocalCashBundles() >= 2)
                AddAction("Tout fusionner", () => { if (NetworkClient.isConnected) NetworkClient.Send(new C2S_MergeAllCash { TargetEntityId = entityId }); });
        }
        else if (src.StartsWith("hand_right:")) {
            // Arme encombrante tenue en main : mêmes actions que le slot hotbar actif.
            AddWeaponActions(item, src);
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
            // Item d'un conteneur ouvert (colis, sac, meuble) : arme → Recharger/Vider
            // (logistique, opère sur place), puis « Prendre » vers l'inventaire — même
            // cible que le quick-move double-clic.
            AddWeaponActions(item, src);
            var draggable = item;
            AddAction("Prendre", () => {
                if (inv.TryFindQuickMoveTargetForItem(draggable, out string placeKey, out int slotIndex))
                    SendMove(draggable.EntityId, src, placeKey, slotIndex);
                else
                    WorldToastManager.ShowError(InventoryToasts.NoSpaceInInventory);
            });
        }
    }

    /// <summary>Actions d'arme à feu de la carte de détail — PURE LOGISTIQUE, sans
    /// animation ni timer, où que soit l'arme (poche, main, conteneur/sac/colis ouvert) :
    /// « Recharger » (chargeur pas plein + des balles sur le joueur — poches, sac à dos
    /// porté ou colis tenu) et « Vider le chargeur » (chargeur non vide → les balles
    /// retournent en piles dans l'inventaire). Toujours les opérations « à froid »
    /// RequestReloadItem/RequestUnloadItem ; le flux ANIMÉ ne subsiste que sur la touche
    /// R en jeu, inventaire fermé.</summary>
    private void AddWeaponActions(DraggableItem item, string src)
    {
        WeaponConfig cfg = item.ItemConfig as WeaponConfig;
        if (cfg == null) return;
        PlayerShooter shooter = PlayerController.Local != null
            ? PlayerController.Local.GetComponent<PlayerShooter>() : null;
        if (shooter == null) return;

        // Chargeur : l'arme TENUE (slot hotbar actif ou main) est suivie en live par le
        // PlayerShooter — le snapshot ne se re-pousse pas à chaque tir ; les autres lisent
        // le LoadedAmmo publié par leur snapshot. -1 = inconnu → proposé, le serveur tranche.
        WeaponBehaviour held = shooter.HeldWeapon;
        bool isHeld = held != null && held.Configuration == item.ItemConfig
            && (src.StartsWith("hand_")
                || (src.StartsWith("pocket:") && item.ItemSlot != null
                    && item.ItemSlot.SlotIndex == HotbarUI.SelectedSlotIndex));
        int loaded = isHeld ? shooter.LocalLoadedAmmo : item.LoadedAmmo;
        int entityId = item.EntityId;

        if ((loaded < 0 || loaded < cfg.MagazineSize) && MayHaveRounds(cfg.AmmoCategory))
            AddAction("Recharger", () => shooter.RequestReloadItem(entityId), Key.R);
        if (loaded != 0)
            AddAction("Vider le chargeur", () => shooter.RequestUnloadItem(entityId));
    }

    /// <summary>Le joueur peut-il avoir des balles de cette famille SUR LUI (périmètre
    /// consommé par le rechargement : poches, puis sac à dos porté ou colis tenu) ?
    /// Les piles de poche sont visibles dans le snapshot ; le contenu d'un sac/colis
    /// fermé ne l'est pas → sa seule présence rend la réponse optimiste, le serveur
    /// tranche (toast « Aucune munition compatible sur vous » si vide).</summary>
    private static bool MayHaveRounds(AmmoCategory category)
    {
        if (ClientItemManager.LastPocketSnapshot.HasValue) {
            S2C_PocketSync snapshot = ClientItemManager.LastPocketSnapshot.Value;
            if (snapshot.Items != null) {
                foreach (S2C_PocketItem it in snapshot.Items)
                    if (DatabaseManager.GetItemConfigById(it.ConfigId) is AmmoConfig ac && ac.AmmoCategory == category)
                        return true;
            }
        }
        var hands = PlayerController.Local != null ? PlayerController.Local.PlayerHands : null;
        if (hands == null) return false;
        return IsContainerItem(hands.BackItem) || IsContainerItem(hands.RightHandItem) || IsContainerItem(hands.LeftHandItem);
    }

    /// <summary>Vrai si l'item tenu/porté est un conteneur (sac, colis) susceptible de
    /// cacher des balles.</summary>
    private static bool IsContainerItem(ItemBehaviour item)
    {
        if (item == null || item.Configuration == null) return false;
        ContainerConfig cc = ItemContainerConfig.Of(item.Configuration);
        return cc != null && cc.IsContainer;
    }

    /// <summary>Nombre de liasses distinctes SUR le joueur : lignes cash de la poche +
    /// liasse tenue si ce n'est pas la projection hotbar d'un slot déjà compté — même
    /// déduplication que le serveur (MergeAllCash / TakeCashAmount).</summary>
    private static int CountLocalCashBundles()
    {
        int count = 0;
        bool selectedSlotIsCash = false;
        if (ClientItemManager.LastPocketSnapshot.HasValue) {
            S2C_PocketSync snapshot = ClientItemManager.LastPocketSnapshot.Value;
            if (snapshot.Items != null) {
                foreach (S2C_PocketItem it in snapshot.Items) {
                    if (it.ConfigId != CashItem.ConfigId) continue;
                    count++;
                    if (it.SlotIndex == HotbarUI.SelectedSlotIndex) selectedSlotIsCash = true;
                }
            }
        }
        var hands = PlayerController.Local != null ? PlayerController.Local.PlayerHands : null;
        if (hands != null && !selectedSlotIsCash
            && (hands.RightHandItem is CashItemBehaviour || hands.LeftHandItem is CashItemBehaviour))
            count++;
        return count;
    }

    private void AddAction(string label, System.Action onClick, Key key = Key.None)
    {
        Button b = Instantiate(actionButtonTemplate, actionsContainer);
        b.gameObject.SetActive(true);
        // Libellé (cible explicite pour ne pas confondre avec le TMP de la pastille).
        Transform labelTr = b.transform.Find("Label");
        TMP_Text txt = labelTr != null ? labelTr.GetComponent<TMP_Text>() : b.GetComponentInChildren<TMP_Text>(true);
        if (txt) txt.text = label;
        // Pastille de touche : affichée seulement si l'action a un raccourci.
        Transform chip = b.transform.Find("Key Chip");
        if (chip != null) {
            bool hasKey = key != Key.None;
            chip.gameObject.SetActive(hasKey);
            if (hasKey) {
                TMP_Text ktxt = chip.GetComponentInChildren<TMP_Text>(true);
                if (ktxt) ktxt.text = KeyLabel(key);
            }
        }
        b.onClick.AddListener(() => onClick?.Invoke());
        if (key != Key.None) _keyActions.Add((key, onClick));
        _buttons.Add(b);
    }

    /// <summary>Libellé court d'une touche pour la pastille (ex. Alt, Maj, Espace).</summary>
    private static string KeyLabel(Key key)
    {
        switch (key) {
            case Key.LeftAlt: case Key.RightAlt:     return "Alt";
            case Key.LeftShift: case Key.RightShift: return "Maj";
            case Key.LeftCtrl: case Key.RightCtrl:   return "Ctrl";
            case Key.Space:                          return "Espace";
            default:                                 return key.ToString();
        }
    }

    private static void SendMove(int entityId, string from, string to, int toSlot)
    {
        if (!NetworkClient.isConnected || entityId <= 0) return;
        NetworkClient.Send(new C2S_MoveItem {
            EntityId = entityId, FromPlaceId = from, ToPlaceId = to, ToSlotIndex = toSlot,
        });
    }
}
