using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DraggableItem : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IEndDragHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler {
    private Canvas canvas;
    private Image _image;
    private RectTransform _rectTransform;
    private CanvasGroup _canvasGroup;
    private ItemSlot _itemSlot;
    private ItemConfig _itemConfig;

    [Header("Stack badge")]
    [SerializeField, Tooltip("Texte « xN » affiché en coin du slot quand la pile a plus d'1 unité. " +
        "Désactivé par défaut ; le draggable l'active/désactive via SetQuantity. Authoré dans le prefab.")]
    private TMPro.TMP_Text quantityBadge;

    private int _quantity = 1;

    // Arme : balles dans le chargeur, publié par les snapshots serveur (poche/conteneur).
    // -1 = inconnu ici — la tooltip retombe alors sur le PlayerShooter local si ce
    // draggable représente l'arme actuellement tenue (slots mains, pas de snapshot).
    private int _loadedAmmo = -1;
    public void SetLoadedAmmo(int loaded) => _loadedAmmo = loaded;
    /// <summary>Chargeur publié par le snapshot (-1 = inconnu). Lu par la carte de détail
    /// pour gater Recharger/Vider sur une arme NON tenue.</summary>
    public int LoadedAmmo => _loadedAmmo;

    // Liasse : montant publié par le snapshot poche (S2C_PocketItem.CashAmount). 0 = inconnu
    // ici — la tooltip retombe alors sur l'entité monde (liasse tenue ou au sol).
    private int _cashAmount;
    public void SetCashAmount(int amount) => _cashAmount = amount;
    /// <summary>Taille de pile (≥1). 1 = item simple, pas de badge. >1 = badge "xN" visible
    /// + règles stack (split-1 vers main/poche, refus swap pile↔main).</summary>
    public int Quantity => _quantity;
    public void SetQuantity(int q) {
        _quantity = Mathf.Max(1, q);
        RefreshQuantityBadge();
    }
    private void RefreshQuantityBadge() {
        if (quantityBadge == null) return;
        bool show = _quantity > 1;
        quantityBadge.gameObject.SetActive(show);
        if (show) quantityBadge.text = _quantity.ToString();
    }

    // ── Animation parameters (tunable per-prefab) ─────────────────────────────
    [Header("Animation — Lift au pickup")]
    [SerializeField, Tooltip("Échelle du draggable pendant un drag.")]
    private float liftScale = 1.08f;
    [SerializeField, Tooltip("Durée de la transition d'échelle (pickup et release).")]
    private float liftDuration = 0.08f;

    [Header("Animation — Land au drop valide")]
    [SerializeField, Tooltip("Durée du tween vers le centre du slot après un drop valide.")]
    private float landDuration = 0.14f;
    [SerializeField, Tooltip("Courbe d'easing du land. EaseInOut par défaut ; tu peux la remplacer par une courbe avec léger overshoot pour un effet plus tactile.")]
    private AnimationCurve landCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Animation — Snap-back drop invalide")]
    [SerializeField, Tooltip("Durée du retour à l'origine quand le drop échoue.")]
    private float snapBackDuration = 0.18f;
    [SerializeField, Tooltip("Courbe d'easing du snap-back.")]
    private AnimationCurve snapBackCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public float LandDuration         => landDuration;
    public AnimationCurve LandCurve   => landCurve;
    public float SnapBackDuration       => snapBackDuration;
    public AnimationCurve SnapBackCurve => snapBackCurve;

    /// <summary>
    /// Évalue <paramref name="curve"/> à <paramref name="rawT"/> ∈ [0,1] avec un fallback
    /// linéaire si la courbe est vide (cas d'un prefab sérialisé avant que les champs
    /// d'animation existent). Sans ce garde-fou, Evaluate retourne 0 partout et le tween
    /// ne bouge pas visuellement.
    /// </summary>
    private static float SafeEvaluate(AnimationCurve curve, float rawT) {
        if (curve == null || curve.length == 0) return rawT;
        return curve.Evaluate(rawT);
    }

    private Coroutine _moveTween;
    private Coroutine _scaleTween;

    public delegate void Draggable(DraggableItem item);

    public static event Draggable OnLeftClick;

    public static event Draggable OnRightClick;

    /// <summary>
    /// Double clic gauche sur le draggable — consommé par les UI d'inventaire pour
    /// router un quick-move (main/poche → conteneur, conteneur → main/poche). Le
    /// premier clic continue de fire OnLeftClick normalement.
    /// </summary>
    public static event Draggable OnDoubleClick;

    public static event Draggable OnStartDrag;

    /// <summary>
    /// Émis à la fin d'un drag (qu'il soit suivi d'un drop valide, d'un snap-back ou d'une
    /// annulation). Consommé par les UI d'inventaire pour restaurer le visuel des slots
    /// désactivés pendant le drag (drop acceptance reset).
    /// </summary>
    public static event Draggable OnDragEnded;

    private void Awake() {
        EnsureRefs();
    }

    /// <summary>
    /// Cache les composants nécessaires. Appelé idempotemment depuis chaque entry-point
    /// public (SetConfiguration, OnBeginDrag, …) car ces draggables peuvent être
    /// instanciés dans une hiérarchie inactive (parent désactivé) — auquel cas Awake
    /// ne tourne PAS et tous les champs cachés sont null jusqu'à l'activation. Cette
    /// méthode est l'auto-réparation : zéro coût si déjà initialisé.
    /// </summary>
    private void EnsureRefs() {
        if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
        if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
        if (_image == null) _image = GetComponent<Image>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
    }

    // EntityId côté serveur (utilisé pour C2S_MoveItem). 0 = inconnu / pas encore mappé.
    private int _entityId;
    public int EntityId => _entityId;
    public void SetEntityId(int entityId) => _entityId = entityId;

    // false = entrée d'affichage non-déplaçable (ex. meuble emballé dans un colis : sa
    // sortie passe par le déballage build-mode, pas un drag). Remis à true au release.
    private bool _draggable = true;
    public bool IsDraggable => _draggable;
    public void SetDraggable(bool value) => _draggable = value;

    // Vrai seulement si OnBeginDrag a réellement démarré un drag : Unity appelle
    // OnDrag/OnEndDrag même quand OnBeginDrag a refusé (entrée verrouillée — slot
    // main indicatif, meuble emballé) — sans ce flag l'item suivrait le curseur.
    private bool _dragging;

    // Meuble emballé (entrée de colis) : id PropsConfig + variant pour démarrer le déballage.
    private int _propConfigId;
    private int _propPresetId;
    public int  PropConfigId => _propConfigId;
    public int  PropPresetId => _propPresetId;
    public bool IsPackedProp => _propConfigId > 0;
    public void SetPackedProp(int propConfigId, int propPresetId) {
        _propConfigId = propConfigId;
        _propPresetId = propPresetId;
    }

    public void SetConfiguration(ItemConfig itemConfig) {
        EnsureRefs();
        this._itemConfig = itemConfig;
        this._image.sprite = this._itemConfig != null ? this._itemConfig.Icon : null;
    }

    /// <summary>Force un sprite arbitraire (ex. icône d'un meuble emballé via PropsConfig).</summary>
    public void SetSprite(Sprite sprite) {
        EnsureRefs();
        this._image.sprite = sprite;
    }

    // ── Contenu descriptif (partagé tooltip survol + carte de détail) ──────────

    /// <summary>Construit le contenu descriptif de l'item : icône, nom, description,
    /// effets (carburant/arme/munitions/conso + pile) et résumé conteneur. Même
    /// logique pour la tooltip au survol et la carte de détail de l'inventaire.</summary>
    public void BuildDetailContent(out Sprite icon, out string label, out string desc,
        out string effects, out string container) {
        icon = null; label = null; desc = null; effects = null; container = null;

        if (_propConfigId > 0) {
            var p = Sim.DatabaseManager.GetPropsById(_propConfigId);
            if (p != null) { icon = p.Sprite; label = p.GetDisplayName(); desc = p.Description; container = FormatContainer(p.Container); }
        } else if (_itemConfig != null) {
            icon = _itemConfig.Icon; label = _itemConfig.Label; desc = _itemConfig.Description;
            if (_itemConfig.ID == FuelCanister.ConfigId) effects = FormatCanisterFuel(_entityId, _itemConfig);
            else if (_itemConfig.ID == CashItem.ConfigId) effects = FormatCashAmount(_entityId);
            else if (_itemConfig is WeaponConfig weaponCfg) effects = FormatWeaponAmmo(weaponCfg);
            else if (_itemConfig is AmmoConfig ammoCfg) effects = FormatAmmoBox(ammoCfg);
            else effects = FormatEffects(_itemConfig as ConsumableConfig);
            container = FormatContainer(Sim.Scriptables.ItemContainerConfig.Of(_itemConfig));
        }
        // Pile : affiche "Pile : N" en tête des effects pour qu'on le voie immédiatement.
        if (_quantity > 1) {
            string stackLine = $"<color=#8FE36B>Pile : {_quantity}</color>";
            effects = string.IsNullOrEmpty(effects) ? stackLine : stackLine + "\n" + effects;
        }
    }

    // ── Tooltip au survol (icône + nom + description + effets + conteneur) ─────
    public void OnPointerEnter(PointerEventData eventData) {
        BuildDetailContent(out Sprite icon, out string label, out string desc, out string effects, out string container);
        if (icon != null || !string.IsNullOrEmpty(label)) Sim.ItemTooltipUI.Show(icon, label, desc, effects, container);
    }

    /// <summary>Met en forme les infos conteneur (type de stockage, capacité, espace restant).
    /// L'espace restant n'est réel que si le panneau de CE conteneur est ouvert (sinon capacité).</summary>
    private string FormatContainer(Sim.Scriptables.ContainerConfig cc) {
        if (cc == null || !cc.IsContainer) return null;
        // Espace restant réel si le panneau de CE conteneur est ouvert, sinon capacité.
        int free = -1;
        var panel = ContainerPanelUI.Item;
        if (panel != null && panel.IsShowingItemContainer(_entityId)) free = panel.FreeSlotCount();
        return Sim.ItemTooltipUI.FormatContainer(cc, free);
    }

    public void OnPointerExit(PointerEventData eventData) {
        Sim.ItemTooltipUI.Hide();
    }

    /// <summary>Contenu du bidon d'essence (litres restants) pour la tooltip d'inventaire.
    /// Le niveau est répliqué sur l'ItemBehaviour côté client (S2C_ItemFuel).</summary>
    private static string FormatCanisterFuel(int entityId, ItemConfig cfg) {
        var item = ClientItemManager.Instance.GetItem(entityId) as FuelCanisterBehaviour;
        float fuel = item != null ? item.Fuel : 0f;
        float capacity = (cfg as FuelCanisterConfig)?.fuelCapacity ?? 20f;
        float pct = capacity > 0f ? Mathf.Clamp01(fuel / capacity) : 0f;
        string color = pct <= 0.001f ? "#E8836B" : (pct < 0.34f ? "#E8C36B" : "#8FE36B");
        return $"<color={color}>Réservoir : {fuel:0} / {capacity:0} L</color>";
    }

    /// <summary>Montant d'une liasse de billets pour la tooltip. En poche il vient du
    /// snapshot (_cashAmount) ; sinon de l'entité monde — liasse TENUE ou au sol — répliquée
    /// via S2C_ItemCash. Inconnu → pas de ligne plutôt que « 0 € ».</summary>
    private string FormatCashAmount(int entityId) {
        int amount = _cashAmount;
        if (amount <= 0 && ClientItemManager.Instance != null
            && ClientItemManager.Instance.GetItem(entityId) is CashItemBehaviour cash)
            amount = cash.Amount;
        if (amount <= 0) return null;
        return $"<color=#8FE36B>Valeur : {MoneyFormat.FormatBalance(amount)}</color>";
    }

    /// <summary>Détails d'une arme pour la tooltip : famille de munitions compatible + état
    /// du chargeur. Le chargeur vient du snapshot d'inventaire (_loadedAmmo, poche/conteneur) ;
    /// pour l'arme TENUE (slots mains, pas de snapshot) il vient du PlayerShooter local.</summary>
    private string FormatWeaponAmmo(WeaponConfig cfg) {
        int loaded = _loadedAmmo;
        if (loaded < 0 && Sim.PlayerController.Local != null) {
            var shooter = Sim.PlayerController.Local.GetComponent<Sim.PlayerShooter>();
            var behaviour = ClientItemManager.Instance != null
                ? ClientItemManager.Instance.GetItem(_entityId) as WeaponBehaviour : null;
            if (shooter != null && behaviour != null && shooter.HeldWeapon == behaviour)
                loaded = shooter.LocalLoadedAmmo;
        }

        string magazine;
        if (loaded < 0) {
            magazine = $"Chargeur : ? / {cfg.MagazineSize}";
        } else {
            float pct = cfg.MagazineSize > 0 ? Mathf.Clamp01((float)loaded / cfg.MagazineSize) : 0f;
            string color = loaded == 0 ? "#E8836B" : (pct < 0.34f ? "#E8C36B" : "#8FE36B");
            magazine = $"<color={color}>Chargeur : {loaded} / {cfg.MagazineSize}</color>";
        }
        return $"Munitions : {AmmoConfig.CategoryLabel(cfg.AmmoCategory)}\n{magazine}";
    }

    /// <summary>Détails d'une pile de balles pour la tooltip : famille servie + règle de
    /// rechargement (le chargeur prélève les balles nécessaires).</summary>
    private static string FormatAmmoBox(AmmoConfig cfg) {
        return $"Munitions : {AmmoConfig.CategoryLabel(cfg.AmmoCategory)}\n<color=#8FE36B>Le rechargement prélève les balles nécessaires</color>";
    }

    /// <summary>Met en forme les effets d'un consommable (faim/soif/fatigue) pour la tooltip.</summary>
    private static string FormatEffects(ConsumableConfig cc) {
        if (cc == null || cc.Impacts == null || cc.Impacts.Length == 0) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var hv in cc.Impacts) {
            if (hv == null) continue;
            string n;
            switch (hv.VitalNecessityType) {
                case VitalNecessityType.HUNGRY:    n = "Faim";    break;
                case VitalNecessityType.THIRST:    n = "Soif";    break;
                case VitalNecessityType.TIREDNESS: n = "Fatigue"; break;
                default: n = hv.VitalNecessityType.ToString();    break;
            }
            float v = hv.Value;
            string color = v >= 0 ? "#8FE36B" : "#E8836B";
            sb.AppendLine($"<color={color}>{n} {(v >= 0 ? "+" : "")}{v:0.#}</color>");
        }
        return sb.ToString().TrimEnd();
    }

    public void OnBeginDrag(PointerEventData eventData) {
        EnsureRefs();
        if (!_draggable) return; // entrée verrouillée (slot main, meuble emballé) : pas de drag
        _dragging = true;
        Sim.ItemTooltipUI.Hide(); // pas de tooltip pendant un drag
        this._canvasGroup.alpha = .6f;
        this._canvasGroup.blocksRaycasts = false;
        // Reparent au groupe inventaire (ou Canvas en fallback) pour rester au-dessus
        // de tous les panels frères de l'inventaire ET hors de tout Mask pendant le drag.
        // Même helper que ItemSlot utilise pour le land/snap-back → cohérence drag + anim.
        Transform animParent = ItemSlot.ResolveTopLevelAnimParent(this);
        if (animParent != null) this.transform.SetParent(animParent, worldPositionStays: true);
        this.transform.SetAsLastSibling();
        // Tout tween de position en cours (land/snap-back précédent) doit être stoppé
        // pour ne pas combattre OnDrag qui modifie anchoredPosition à chaque frame.
        if (_moveTween != null) { StopCoroutine(_moveTween); _moveTween = null; }
        AnimateScale(liftScale, liftDuration);
        OnStartDrag?.Invoke(this);
    }

    public void OnDrag(PointerEventData eventData) {
        if (!_dragging) return;
        EnsureRefs();
        if (canvas == null) return;
        this._rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
    }

    public void OnPointerClick(PointerEventData eventData) {
        switch (eventData.button) {
            case PointerEventData.InputButton.Left:
                OnLeftClick?.Invoke(this);
                // Unity's EventSystem fournit clickCount sur le second clic d'une
                // séquence (timer interne du StandaloneInputModule). On émet DoubleClick
                // en plus de LeftClick — les handlers existants continuent de marcher.
                if (eventData.clickCount == 2) OnDoubleClick?.Invoke(this);
                break;

            case PointerEventData.InputButton.Right:
                OnRightClick?.Invoke(this);
                break;
        }
    }

    public void OnEndDrag(PointerEventData eventData) {
        if (!_dragging) return;
        _dragging = false;
        EnsureRefs();
        this._canvasGroup.alpha = 1;
        this._canvasGroup.blocksRaycasts = true;
        AnimateScale(1f, liftDuration);
        // Notifie les listeners (InventoryUI, ContainerPanelUI) avant la logique
        // snap-back pour qu'ils puissent restaurer les slots désactivés pendant le drag.
        OnDragEnded?.Invoke(this);

        // Drop out of any slot → snap-back animé vers l'origine.
        // (ItemSlot.OnDrop gère le cas où le drop tombe SUR un slot ; il fire avant
        // OnEndDrag dans l'event order. Si on arrive ici sans qu'aucun slot n'ait
        // attrapé le drop, on revient à l'origine en tween.)
        if (IsOutOfSlot(eventData) && _itemSlot != null) {
            // Cible monde = centre du slot d'origine.
            Vector3 targetWorld = _itemSlot.transform.position;
            // On reste au niveau Canvas (déjà le parent grâce à OnBeginDrag) pendant le
            // tween : reparenter directement sous _itemSlot ferait clipper le draggable
            // par un éventuel Mask du panel (Container Panel.Slots Scroll View) tant que
            // l'item n'est pas arrivé à destination. Le reparent final se fait dans onComplete.
            transform.SetAsLastSibling();
            var slot = _itemSlot;
            AnimateToWorldPosition(targetWorld, snapBackDuration, snapBackCurve, onComplete: () => {
                if (slot != null) slot.TryFinishLand(this);
            });
        }
    }

    public ItemSlot ItemSlot {
        get => _itemSlot;
        set => _itemSlot = value;
    }

    public ItemConfig ItemConfig => _itemConfig;

    public void SetAnchoredPosition(Vector2 pos) {
        EnsureRefs();
        this._rectTransform.anchoredPosition = pos;
    }

    public void SetPadding(float top, float right, float bottom, float left) {
        EnsureRefs();
        this._rectTransform.offsetMin = new Vector2(left, bottom);
        this._rectTransform.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>
    /// Remet le draggable dans un état neutre (alpha=1, raycast actif, scale=1, tweens stoppés).
    /// À appeler impérativement avant de le rendre au pool : si le GameObject est
    /// désactivé (via SetActive) à l'intérieur du OnDrop, Unity ne déclenche jamais
    /// OnEndDrag sur l'objet désactivé et l'état de drag reste coincé dans le pool.
    /// </summary>
    public void ResetDragState() {
        EnsureRefs();
        if (_canvasGroup == null) return;
        _canvasGroup.alpha = 1f;
        _canvasGroup.blocksRaycasts = true;
        if (_moveTween  != null) { StopCoroutine(_moveTween);  _moveTween  = null; }
        if (_scaleTween != null) { StopCoroutine(_scaleTween); _scaleTween = null; }
        transform.localScale = Vector3.one;
        _draggable = true; // réinitialise pour la prochaine location depuis le pool
        _dragging  = false;
        _propConfigId = 0;
        _propPresetId = 0;
        // Le pool peut réémettre un draggable déjà utilisé avec un Quantity hérité — reset à 1.
        SetQuantity(1);
        _loadedAmmo = -1; // idem pour le chargeur publié par un snapshot précédent
        _cashAmount = 0;  // idem pour le montant de liasse
        Sim.ItemTooltipUI.Hide(); // au cas où ce draggable était survolé au moment du release
    }

    private bool IsOutOfSlot(PointerEventData eventData) {
        return this._itemSlot && (!eventData.pointerCurrentRaycast.isValid || eventData.pointerCurrentRaycast.gameObject.GetComponent<ItemSlot>() == null);
    }

    // ── Tween helpers (coroutine + AnimationCurve, même pattern que ContainerDoorAnimator) ──
    //
    // L'animation de position se fait sur transform.position (monde) plutôt que sur
    // anchoredPosition : robuste face au mode d'ancrage du draggable (stretched ou
    // pivot-centré), et indépendant de SetPadding qui sinon snape instantanément à 0.
    // L'appelant doit reparenter AVANT l'anim (avec worldPositionStays:true pour ne
    // pas teleporter au moment du change-parent), puis demander l'anim vers la position
    // monde du slot cible, et appliquer SetPadding dans le callback onComplete.

    /// <summary>Anime <c>transform.position</c> (coordonnées monde) vers <paramref name="targetWorld"/>.
    /// Robuste face à n'importe quelle config d'ancrage du RectTransform. À la fin, exécute
    /// <paramref name="onComplete"/> (typiquement : SetPadding + recentrage propre).</summary>
    public void AnimateToWorldPosition(Vector3 targetWorld, float duration, AnimationCurve curve,
        System.Action onComplete = null)
    {
        EnsureRefs();
        if (_moveTween != null) { StopCoroutine(_moveTween); _moveTween = null; }
        if (duration <= 0f || !isActiveAndEnabled) {
            transform.position = targetWorld;
            onComplete?.Invoke();
            return;
        }
        _moveTween = StartCoroutine(WorldMoveCoroutine(targetWorld, duration, curve, onComplete));
    }

    /// <summary>Anime <c>transform.localScale</c> de manière uniforme vers <paramref name="target"/>.</summary>
    public void AnimateScale(float target, float duration) {
        EnsureRefs();
        if (_scaleTween != null) { StopCoroutine(_scaleTween); _scaleTween = null; }
        Vector3 endScale = Vector3.one * target;
        if (duration <= 0f || !isActiveAndEnabled) {
            transform.localScale = endScale;
            return;
        }
        _scaleTween = StartCoroutine(ScaleCoroutine(endScale, duration));
    }

    private IEnumerator WorldMoveCoroutine(Vector3 target, float duration, AnimationCurve curve,
        System.Action onComplete)
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < duration) {
            // unscaledDeltaTime : indépendant de Time.timeScale (l'UI doit rester animée en pause).
            t += Time.unscaledDeltaTime;
            float k = SafeEvaluate(curve, Mathf.Clamp01(t / duration));
            transform.position = Vector3.LerpUnclamped(start, target, k);
            yield return null;
        }
        transform.position = target;
        _moveTween = null;
        onComplete?.Invoke();
    }

    private IEnumerator ScaleCoroutine(Vector3 target, float duration) {
        Vector3 start = transform.localScale;
        float t = 0f;
        while (t < duration) {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            transform.localScale = Vector3.LerpUnclamped(start, target, k);
            yield return null;
        }
        transform.localScale = target;
        _scaleTween = null;
    }
}