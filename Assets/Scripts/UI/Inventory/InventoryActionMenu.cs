using System.Collections.Generic;
using DG.Tweening;
using Sim.Interactables;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu contextuel procédural (clic droit) : reconstruit ses boutons à partir de la liste
/// d'actions et s'affiche À CÔTÉ DU CURSEUR. Le coin d'ancrage (pivot) bascule selon le
/// quadrant écran du curseur pour que le menu s'ouvre toujours vers l'intérieur.
/// </summary>
public class InventoryActionMenu : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Transform contentTransform;

    [SerializeField]
    private InventoryActionButton inventoryActionButtonPrefab;

    [Tooltip("Décalage (px canvas) entre le curseur et le coin du menu.")]
    [SerializeField]
    private Vector2 cursorOffset = new Vector2(8f, -8f);

    [Header("Only for debug")]
    [SerializeField]
    private List<InventoryActionButton> inventoryActionButtons;

    private CanvasGroup _canvasGroup;
    private Canvas      _canvas;
    private RectTransform _rect;

    /// <summary>Instance partagée du menu contextuel, réutilisable par d'autres UI
    /// (ex. ContainerPanelUI clic droit). Une des instances HUD authorées sert de menu.</summary>
    public static InventoryActionMenu Shared { get; private set; }

    private void Awake() {
        this.inventoryActionButtons = new List<InventoryActionButton>();
        this._canvasGroup = GetComponent<CanvasGroup>();
        this._rect        = (RectTransform)transform;
        this._canvas      = GetComponentInParent<Canvas>();

        // Sous-canvas dédié avec tri prioritaire : le menu contextuel s'affiche AU-DESSUS
        // de tout le HUD (sinon il passe sous le panneau conteneur ouvert).
        Canvas own = GetComponent<Canvas>();
        if (own == null) {
            own = gameObject.AddComponent<Canvas>();
            if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }
        own.overrideSorting = true;
        own.sortingOrder    = 1000;

        Shared = this;

        this.Hide(true);
    }

    public void Setup(List<Action> actions) {
        this.ClearButtons();

        actions.ForEach(action => {
            InventoryActionButton button = Instantiate(this.inventoryActionButtonPrefab, this.contentTransform);

            button.Setup(action);

            this.inventoryActionButtons.Add(button);
        });

        // Fitters imbriqués (boutons → Content → root) : on force un rebuild immédiat pour
        // que la taille du menu soit correcte AVANT le positionnement au curseur.
        LayoutRebuilder.ForceRebuildLayoutImmediate(this._rect);

        this.PositionAtCursor();
        this._canvasGroup.blocksRaycasts = true;
        this._canvasGroup.interactable = true;
        this._canvasGroup.DOFade(1, .3f);
        // Le clic qui a OUVERT le menu peut encore être détecté par Update ce même frame.
        // On le mémorise pour ignorer le close-on-outside-click pendant cette frame.
        this._openedFrame = Time.frameCount;
    }

    // Frame d'ouverture pour éviter qu'un clic d'ouverture (Input.GetMouseButtonDown
    // toujours true à la frame de Setup) referme aussitôt le menu.
    private int _openedFrame = -1;

    /// <summary>Ferme le menu si l'utilisateur clique (gauche ou droit) HORS du rect du menu.
    /// Les clics sur les boutons internes restent gérés par eux (OnPointerClick).</summary>
    private void Update() {
        if (this._canvasGroup == null || !this._canvasGroup.blocksRaycasts) return;
        if (Time.frameCount == this._openedFrame) return; // skip same-frame as Show
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;

        if (this._rect == null) return;
        Camera cam = (this._canvas != null && this._canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? this._canvas.worldCamera : null;
        bool inside = RectTransformUtility.RectangleContainsScreenPoint(this._rect, Input.mousePosition, cam);
        if (inside) return; // click ON a button → laisse l'event system propager

        this.DisposeTransient();
        this.Hide();
    }

    /// <summary>Place le menu au niveau du curseur. Le pivot bascule vers le centre écran
    /// pour que le menu s'ouvre toujours vers l'intérieur (jamais hors écran).</summary>
    public void PositionAtCursor() {
        if (_rect == null) _rect = (RectTransform)transform;
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
        RectTransform parent = _rect.parent as RectTransform;
        if (parent == null) return;

        Camera cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _canvas.worldCamera : null;

        Vector2 mouse = Input.mousePosition;
        // Pivot vers le coin "intérieur" : ouvre le menu vers le centre de l'écran.
        _rect.pivot = new Vector2(mouse.x < Screen.width * 0.5f ? 0f : 1f,
                                  mouse.y < Screen.height * 0.5f ? 0f : 1f);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, mouse, cam, out Vector2 local)) {
            // Décale dans le sens d'ouverture : +x si le menu s'ouvre à droite, -y s'il s'ouvre vers le bas.
            float dirX = _rect.pivot.x == 0f ? 1f : -1f;
            float dirY = _rect.pivot.y == 1f ? -1f : 1f;
            _rect.anchoredPosition = local + new Vector2(Mathf.Abs(cursorOffset.x) * dirX, Mathf.Abs(cursorOffset.y) * dirY);
        }
    }

    // ── Actions transitoires : menu contextuel câblé à des callbacks ad-hoc ──────
    // Réutilisé par ContainerPanelUI (Poser / Lâcher / Diviser) et InventoryUI (Lâcher poche).
    private readonly List<Action> _transientActions = new List<Action>();
    private readonly List<System.Action> _transientCallbacks = new List<System.Action>();

    /// <summary>Affiche le menu avec UNE action clonée de <paramref name="proto"/> ; au clic,
    /// exécute <paramref name="onExecute"/> puis se ferme. Gère le cycle de vie du clone.</summary>
    public void ShowSingleAction(Action proto, System.Action onExecute) {
        if (proto == null) return;
        ShowMultiAction(new List<Action> { proto }, new List<System.Action> { onExecute });
    }

    /// <summary>Affiche un menu à plusieurs actions clonées (<paramref name="protos"/>) chacune
    /// câblée à son callback (<paramref name="onExecutes"/>, même ordre/taille).</summary>
    public void ShowMultiAction(List<Action> protos, List<System.Action> onExecutes) {
        if (protos == null || onExecutes == null || protos.Count == 0) return;
        DisposeTransient();
        var clones = new List<Action>(protos.Count);
        for (int i = 0; i < protos.Count; i++) {
            if (protos[i] == null) continue;
            Action clone = Instantiate(protos[i]);
            clone.OnExecute += OnTransientExecuted;
            _transientActions.Add(clone);
            _transientCallbacks.Add(i < onExecutes.Count ? onExecutes[i] : null);
            clones.Add(clone);
        }
        Setup(clones);
    }

    private void OnTransientExecuted(Action a) {
        int idx = _transientActions.IndexOf(a);
        System.Action cb = (idx >= 0 && idx < _transientCallbacks.Count) ? _transientCallbacks[idx] : null;
        DisposeTransient();
        Hide();
        cb?.Invoke();
    }

    private void DisposeTransient() {
        foreach (var a in _transientActions) {
            if (a == null) continue;
            a.OnExecute -= OnTransientExecuted;
            Destroy(a);
        }
        _transientActions.Clear();
        _transientCallbacks.Clear();
    }

    private void OnDestroy() {
        DisposeTransient();
        if (Shared == this) Shared = null;
    }

    public void Hide(bool instantly = false) {
        // Coupe les raycasts immédiatement : sinon le menu (même invisible) bloque
        // les clics sur les items en dessous via son GraphicRaycaster.
        this._canvasGroup.blocksRaycasts = false;
        this._canvasGroup.interactable = false;
        if (instantly) {
            this._canvasGroup.alpha = 0;
        } else {
            this._canvasGroup.DOFade(0, .3f);
        }
    }
    
    private void ClearButtons() {
        foreach (InventoryActionButton child in this.inventoryActionButtons) {
            Destroy(child.gameObject);
        }

        this.inventoryActionButtons.Clear();
    }
}
