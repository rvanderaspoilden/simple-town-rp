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

    // ── Action transitoire : menu contextuel à UNE action câblée à un callback ────
    // Réutilisé par ContainerPanelUI (Poser / Lâcher) et InventoryUI (Lâcher poche).
    private Action _transientAction;
    private System.Action _transientCallback;

    /// <summary>Affiche le menu avec UNE action clonée de <paramref name="proto"/> ; au clic,
    /// exécute <paramref name="onExecute"/> puis se ferme. Gère le cycle de vie du clone.</summary>
    public void ShowSingleAction(Action proto, System.Action onExecute) {
        if (proto == null) return;
        DisposeTransient();
        _transientAction = Instantiate(proto);
        _transientCallback = onExecute;
        _transientAction.OnExecute += OnTransientExecuted;
        Setup(new List<Action> { _transientAction });
    }

    private void OnTransientExecuted(Action a) {
        System.Action cb = _transientCallback;
        DisposeTransient();
        Hide();
        cb?.Invoke();
    }

    private void DisposeTransient() {
        if (_transientAction == null) return;
        _transientAction.OnExecute -= OnTransientExecuted;
        Destroy(_transientAction);
        _transientAction = null;
        _transientCallback = null;
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
