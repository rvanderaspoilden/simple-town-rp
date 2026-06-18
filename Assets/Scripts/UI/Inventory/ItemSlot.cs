using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler {
    private DraggableItem _item;

    [Header("Hover highlight (optionnel)")]
    [SerializeField, Tooltip("Graphic (Image background, overlay…) qui fade-in quand un draggable survole ce slot. Laisser vide pour désactiver.")]
    private Graphic dropHighlight;
    [SerializeField, Range(0f, 1f), Tooltip("Alpha cible du highlight survol.")]
    private float highlightAlpha = 0.3f;
    [SerializeField, Tooltip("Durée du fade in/out (secondes).")]
    private float highlightFadeDuration = 0.1f;

    private Coroutine _fadeTween;

    // CanvasGroup auto-injecté pour basculer l'acceptation de drop pendant un drag.
    // Découplé du highlight survol : alpha + blocksRaycasts contrôlent l'éligibilité du slot,
    // et bloquent l'OnDrop sans qu'aucun calcul snap-back ne se déclenche depuis ce slot.
    private UnityEngine.CanvasGroup _dropGroup;
    private const float DimmedAlpha = 0.35f;

    // Place backend de ce slot (hand_left:..., hand_right:..., pocket:..., container place id).
    // Sert au routage cross-place via C2S_MoveItem (InventoryUI.OnItemMoved).
    private string _placeId;
    public string PlaceId {
        get => _placeId;
        set => _placeId = value;
    }

    // Index dans la grille du conteneur ; ignoré pour mains/poches (mono-slot).
    private int _slotIndex;
    public int SlotIndex {
        get => _slotIndex;
        set => _slotIndex = value;
    }

    // Seuls les slots mains autorisent le swap visuel immédiat (hand↔hand).
    // Les slots conteneur ne le font pas : le serveur ne supporte pas l'échange
    // atomique, et le snap-back évite une divergence visuel/serveur.
    public bool CanSwap { get; set; } = false;

    [Header("Filtre item")]
    [SerializeField, Tooltip("Si vrai, ce slot rejette tout drop d'un item dont ItemConfig.AllowedInPocket=false. " +
        "À cocher sur les slots poche : bloque côté client move/swap d'un item non-pocketable (sinon le serveur refuse après round-trip + snap-back retardé).")]
    private bool rejectsNonPocketable = false;
    public bool RejectsNonPocketable => rejectsNonPocketable;

    /// <summary>True si le placeId vise un slot de main (hand_left:… ou hand_right:…).</summary>
    private static bool IsHandPlaceId(string placeId)
        => !string.IsNullOrEmpty(placeId)
           && (placeId.StartsWith("hand_left:") || placeId.StartsWith("hand_right:"));

    /// <summary>True si le slot courant contient une pile dans laquelle <paramref name="incoming"/>
    /// peut se merger : même config, config empilable, ET il reste de la place (qty &lt; max).
    /// Quand la pile est PLEINE, on retourne false → l'OnDrop tombe sur la branche
    /// <see cref="MustSwapWith"/>, ce qui permet un swap fallback (règle utilisateur :
    /// « si les deux slots sont full alors tu effectue un swap »).</summary>
    private bool CanMergeStack(DraggableItem incoming) {
        if (_item == null || incoming == null) return false;
        if (_item.ItemConfig == null || incoming.ItemConfig == null) return false;
        if (_item.ItemConfig != incoming.ItemConfig) return false;
        if (!_item.ItemConfig.IsStackable) return false;
        return _item.Quantity < _item.ItemConfig.MaxStackSize;
    }

    public delegate void ItemMoved(ItemSlot origin, ItemSlot target);
    public delegate void ItemsSwapped(ItemSlot slotA, DraggableItem itemA, ItemSlot slotB, DraggableItem itemB);

    public static event ItemMoved OnItemMove;
    /// <summary>
    /// Émis après un swap visuel. Convention : (<paramref name="slotA"/>, <paramref name="itemA"/>)
    /// décrit l'ORIGINE de l'item draggé (itemA était dans slotA avant le drag),
    /// (<paramref name="slotB"/>, <paramref name="itemB"/>) décrit l'item déplacé qui occupait la
    /// cible (itemB était dans slotB). Après swap : itemA est dans slotB, itemB est dans slotA.
    /// Le handler peut donc envoyer au serveur "déplacer A vers slotB et B vers slotA".
    /// </summary>
    public static event ItemsSwapped OnItemSwap;

    public void OnDrop(PointerEventData eventData) {
        if (eventData.pointerDrag == null) return;

        DraggableItem draggableItem = eventData.pointerDrag.GetComponent<DraggableItem>();
        if (!draggableItem) return;

        // Le drag se termine ici (sur ce slot ou un autre) — hide highlight immédiat.
        FadeHighlight(0f);

        ItemSlot originSlot = draggableItem.ItemSlot;

        // Same-slot drop : si l'origine et la cible sont LE MÊME slot, on ne fait QUE
        // re-ancrer (ou libérer un draggable stale). Aucun move/swap réseau.
        //
        // Sans ce garde, scénario reproductible en spammant le drag&drop d'un item de
        // poche sur lui-même : un snapshot serveur arrive pendant le drag, libère
        // _item et rente un nouveau draggable E sur le slot. Le draggable D en cours
        // de drag n'est PAS libéré (il est sous InventoryGroup, plus enfant du slot).
        // OnDrop voit draggableItem=D, _item=E, originSlot=D.ItemSlot=ce slot → entre
        // dans le branch MustSwapWith → émet C2S_SwapItems avec PlaceIdA==PlaceIdB et
        // SlotIndexA==SlotIndexB → 409 backend ("erreur de persistance").
        if (originSlot == this) {
            if (draggableItem == _item) {
                this.SetItem(draggableItem, animate: true);
            } else {
                // Stale draggable : un snapshot a déjà remplacé _item. On release D
                // sans toucher au nouveau _item (E). Le pool est partagé InventoryUI.
                var inv = Sim.HUDManager.Instance != null ? Sim.HUDManager.Instance.InventoryUI : null;
                if (inv != null) inv.ReleaseDraggable(draggableItem);
                else { draggableItem.transform.SetParent(transform, false); draggableItem.gameObject.SetActive(false); }
            }
            return;
        }

        // Bloque les drops d'items non-pocketables sur les slots poche (move ET swap).
        // Symétrique avec PocketPlaceContext.ValidateAsTarget côté serveur : feedback
        // immédiat sans round-trip réseau, et pas de snap-back retardé après refus.
        if (rejectsNonPocketable
            && draggableItem.ItemConfig != null
            && !draggableItem.ItemConfig.AllowedInPocket) {
            WorldToastManager.ShowError(InventoryToasts.NotPocketable);
            if (originSlot != null) originSlot.SnapBackInto(draggableItem);
            return;
        }

        // Garde stack ↔ main (côté client) : un swap impliquant une main et une pile (qty>1)
        // est refusé d'avance — feedback immédiat sans round-trip. Cible OU origine en main +
        // l'autre côté est une pile → snap-back + toast. (Identique au gate serveur dans
        // HandleSwapItems pour éviter le détour PATCH/snap-back différé.)
        if (_item != null && draggableItem != _item) {
            bool originIsHand = originSlot != null && IsHandPlaceId(originSlot.PlaceId);
            bool targetIsHand = IsHandPlaceId(_placeId);
            bool targetIsStack  = _item.Quantity > 1;
            bool draggedIsStack = draggableItem.Quantity > 1;
            if ((originIsHand && targetIsStack) || (targetIsHand && draggedIsStack)) {
                WorldToastManager.ShowError(InventoryToasts.StackPickOneAtATime);
                if (originSlot != null) originSlot.SnapBackInto(draggableItem);
                return;
            }
        }

        // Imbrication de conteneur : désormais AUTORISÉE si le conteneur déposé est vide. Le
        // client ne connaît pas toujours le contenu → on laisse le serveur trancher
        // (GateNestedContainer) ; en cas de refus il renvoie le toast + un snapshot qui
        // reconcilie le visuel. Plus de blocage client ici.

        if (draggableItem == _item) {
            // Même slot → re-ancre animé (drag annulé sur soi-même).
            this.SetItem(draggableItem, animate: true);
        } else if (CanMergeStack(draggableItem)) {
            // Slot occupé par une pile de même config empilable → route comme un MOVE
            // (PAS un swap, quel que soit le CanSwap des deux slots) : le serveur détecte le
            // merge dans HandleMoveItem et dispatch MergeIntoTargetCoroutine. Le draggable
            // d'origine est libéré visuellement ; la pile cible se rechargera via le snapshot
            // serveur poussé après le merge. Si la pile cible est pleine (qty == max), le
            // serveur refuse avec InventoryToasts.SlotFull et resnapshote → reconcilie.
            draggableItem.ItemSlot.Clear();
            this.SetItem(draggableItem, animate: true);
            OnItemMove?.Invoke(originSlot, this);
            Sim.Audio.AudioManager.Instance.PlayUI(Sim.Audio.SfxId.ItemMove);
        } else if (MustSwapWith(draggableItem)) {
            // CanSwap requis sur les deux slots. hand↔hand est traité localement
            // (PlayerHands.Swap), hand↔container et container↔container passent par
            // C2S_SwapItems côté listener. Les deux items tweenent en parallèle.
            DraggableItem displaced = _item;
            originSlot.SetItem(displaced, animate: true);
            this.SetItem(draggableItem, animate: true);
            OnItemSwap?.Invoke(originSlot, draggableItem, this, displaced);
            Sim.Audio.AudioManager.Instance.PlayUI(Sim.Audio.SfxId.ItemSwap);
        } else if (!this._item) {
            // Slot vide → déplace, smooth land.
            draggableItem.ItemSlot.Clear();
            this.SetItem(draggableItem, animate: true);
            OnItemMove?.Invoke(originSlot, this);
            Sim.Audio.AudioManager.Instance.PlayUI(Sim.Audio.SfxId.ItemMove);
        } else {
            // Slot occupé sans swap autorisé → snap-back animé vers l'origine.
            if (originSlot != null) originSlot.SnapBackInto(draggableItem);
        }
    }

    public void SetItem(DraggableItem item) => SetItem(item, animate: false);

    /// <summary>
    /// Place <paramref name="item"/> dans ce slot. Si <paramref name="animate"/> est vrai,
    /// l'item glisse en monde vers le centre du slot (sensation de drop tactile) ; sinon
    /// snap instantané (utilisé par les reconstructions de snapshot serveur).
    ///
    /// Pour l'anim, on utilise <c>SetParent(…, worldPositionStays:true)</c> pour ne pas
    /// teleporter au moment du change-parent, puis l'item tween jusqu'à
    /// <c>this.transform.position</c> ; SetPadding est appliqué dans le callback de fin
    /// pour ne pas snape avant l'arrivée. Le chemin non animé garde l'ancien comportement.
    /// </summary>
    public void SetItem(DraggableItem item, bool animate) {
        this._item = item;
        this._item.ItemSlot = this;

        if (animate) {
            Vector3 targetWorld = this.transform.position;
            // Drop cross-panel : si on reparente directement au slot cible AVANT l'anim,
            // l'item est clippé par le Mask du panel cible (Container Panel.Slots Scroll View),
            // ou rendu sous le panel d'origine selon l'ordre de hiérarchie. On garde le
            // draggable au niveau racine du Canvas pendant le tween (dernier sibling = sur
            // tout), puis on reparente au slot cible dans le onComplete.
            Transform animParent = ResolveTopLevelAnimParent(item);
            item.transform.SetParent(animParent, worldPositionStays: true);
            item.transform.SetAsLastSibling();
            var targetSlot = this;
            item.AnimateToWorldPosition(targetWorld, item.LandDuration, item.LandCurve, onComplete: () => {
                targetSlot.TryFinishLand(item);
            });
        } else {
            item.transform.parent = this.transform;
            item.SetPadding(10, 10, 10, 10);
            item.SetAnchoredPosition(Vector2.zero);
        }
    }

    /// <summary>
    /// Ré-ancre <paramref name="item"/> au slot après un tween land/snap-back. Robuste face
    /// à un snapshot serveur arrivant pendant l'anim : si le slot a été repris par un autre
    /// item (ex. nouveau draggable issu de S2C_PocketSync après un swap refusé), on libère
    /// l'orphelin au pool au lieu de le coller en résidu visuel sur le slot.
    /// Vérifie : (1) le draggable pointe toujours vers ce slot ; (2) le slot porte toujours
    /// ce draggable. Sinon il y a divergence d'autorité → release.
    /// </summary>
    public void TryFinishLand(DraggableItem item) {
        if (item == null || this == null) return;
        if (item.ItemSlot != this) return;              // item réassigné ailleurs → ne touche rien
        if (_item != item) {                             // slot pris par un autre item (snapshot) → orphelin
            var inv = Sim.HUDManager.Instance != null ? Sim.HUDManager.Instance.InventoryUI : null;
            if (inv != null) inv.ReleaseDraggable(item);
            else { item.transform.SetParent(transform, false); item.gameObject.SetActive(false); }
            return;
        }
        item.transform.SetParent(this.transform, worldPositionStays: false);
        item.SetPadding(10, 10, 10, 10);
        item.SetAnchoredPosition(Vector2.zero);
    }

    /// <summary>
    /// Retourne le transform sous lequel placer le draggable pendant un tween land/snap-back.
    /// Priorité : remonte vers un ancêtre nommé <c>InventoryGroupName</c> (regroupe Container Panel
    /// et Player Inventory Panel sous un seul parent dédié), sinon fallback sur la racine du
    /// Canvas. Reparenter pendant l'anim évite que l'item soit clippé par un Mask présent dans
    /// la hiérarchie du slot d'origine/cible, et garantit qu'il reste rendu au-dessus de tous
    /// les panels frères dans le groupe inventaire. Le groupe dédié confine en plus le z-order
    /// au sous-arbre inventaire et n'interfère pas avec les autres panels HUD.
    /// </summary>
    public const string InventoryGroupName = "Inventory Group";
    internal static Transform ResolveTopLevelAnimParent(DraggableItem item) {
        Transform t = item.transform.parent;
        while (t != null) {
            if (t.name == InventoryGroupName) return t;
            if (t.GetComponent<Canvas>() != null) return t; // fallback : Canvas racine
            t = t.parent;
        }
        return item.transform.parent;
    }

    /// <summary>
    /// Snap-back animé : l'item revient à ce slot (son origine) après un drop invalide.
    /// Utilise <see cref="DraggableItem.SnapBackDuration"/> / <see cref="DraggableItem.SnapBackCurve"/>.
    /// </summary>
    public void SnapBackInto(DraggableItem item) {
        this._item = item;
        this._item.ItemSlot = this;

        Vector3 targetWorld = this.transform.position;
        // Même raison que SetItem(animated) : on reste au niveau Canvas pendant le tween pour
        // éviter le clip/render-under quand on snap-back depuis un panel vers un autre.
        Transform animParent = ResolveTopLevelAnimParent(item);
        item.transform.SetParent(animParent, worldPositionStays: true);
        item.transform.SetAsLastSibling();
        var targetSlot = this;
        item.AnimateToWorldPosition(targetWorld, item.SnapBackDuration, item.SnapBackCurve, onComplete: () => {
            targetSlot.TryFinishLand(item);
        });
    }

    public DraggableItem Item => _item;

    public void Clear() {
        this._item = null;
    }

    /// <summary>
    /// Active ou désactive ce slot comme cible de drop pendant un drag en cours.
    /// Quand <paramref name="ok"/> est faux : alpha réduit (feedback visuel "non éligible")
    /// et <c>blocksRaycasts=false</c> → l'OnDrop ne fire pas, le draggable retombe en
    /// snap-back vers son origine sans calcul serveur ni tentative de swap visuel.
    /// Idempotent ; safe à appeler avant Awake (auto-injection du CanvasGroup).
    /// </summary>
    public void SetDropAcceptance(bool ok) {
        EnsureDropGroup();
        _dropGroup.alpha          = ok ? 1f : DimmedAlpha;
        _dropGroup.blocksRaycasts = ok;
        _dropGroup.interactable   = ok;
    }

    private void EnsureDropGroup() {
        if (_dropGroup != null) return;
        _dropGroup = GetComponent<UnityEngine.CanvasGroup>();
        if (_dropGroup == null) _dropGroup = gameObject.AddComponent<UnityEngine.CanvasGroup>();
    }

    private bool MustSwapWith(DraggableItem draggableItem) {
        if (!CanSwap) return false;
        var originSlot = draggableItem.ItemSlot;
        if (originSlot == null || !originSlot.CanSwap) return false;
        return this._item && this._item != draggableItem;
    }

    private void Awake() {
        // Démarre invisible — sinon un highlight rouge oublié dans le prefab resterait
        // affiché en permanence.
        if (dropHighlight != null) {
            Color c = dropHighlight.color;
            c.a = 0f;
            dropHighlight.color = c;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (eventData.pointerDrag == null) return;
        // Seuls les drags d'un DraggableItem déclenchent le highlight (ignore les
        // pointerDrag d'autres systèmes UI éventuels).
        if (eventData.pointerDrag.GetComponent<DraggableItem>() == null) return;
        FadeHighlight(highlightAlpha);
    }

    public void OnPointerExit(PointerEventData eventData) {
        // Pendant un drag, on hide quand le pointeur sort. Hors drag, no-op (l'alpha
        // est déjà 0).
        if (eventData.pointerDrag == null) return;
        FadeHighlight(0f);
    }

    private void FadeHighlight(float targetAlpha) {
        if (dropHighlight == null) return;
        if (_fadeTween != null) { StopCoroutine(_fadeTween); _fadeTween = null; }
        if (highlightFadeDuration <= 0f || !isActiveAndEnabled) {
            Color c = dropHighlight.color;
            c.a = targetAlpha;
            dropHighlight.color = c;
            return;
        }
        _fadeTween = StartCoroutine(FadeHighlightCoroutine(targetAlpha));
    }

    private IEnumerator FadeHighlightCoroutine(float targetAlpha) {
        Color start = dropHighlight.color;
        float t = 0f;
        while (t < highlightFadeDuration) {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / highlightFadeDuration);
            Color c = start;
            c.a = Mathf.Lerp(start.a, targetAlpha, k);
            dropHighlight.color = c;
            yield return null;
        }
        Color end = dropHighlight.color;
        end.a = targetAlpha;
        dropHighlight.color = end;
        _fadeTween = null;
    }
}
