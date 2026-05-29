using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Rend une fenêtre UI déplaçable au pointeur. À poser sur la "barre de titre"
/// (header) du panneau : OnBeginDrag capture l'offset entre le curseur et le pivot
/// de <see cref="panelRoot"/>, OnDrag met à jour <c>anchoredPosition</c> en
/// préservant cet offset.
///
/// Garde la fenêtre dans les limites du Canvas parent (clamp) — sinon il est
/// possible de la perdre hors écran. Persiste éventuellement la position entre
/// ouvertures via <see cref="PersistKey"/> (PlayerPrefs).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DraggablePanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IInitializePotentialDragHandler
{
    [Tooltip("RectTransform à déplacer. Défaut = transform parent (le panneau).")]
    [SerializeField] private RectTransform panelRoot;

    [Tooltip("Si vrai, restreint la position du panneau aux bornes du Canvas parent (la barre de titre reste visible).")]
    [SerializeField] private bool clampToCanvas = true;

    [Tooltip("Clé optionnelle pour persister la position via PlayerPrefs. Laisse vide pour ne pas persister.")]
    [SerializeField] private string persistKey = "";

    public string PersistKey => persistKey;

    private RectTransform _canvasRect;
    private Vector2 _pointerOffsetLocal;

    private void Awake()
    {
        if (panelRoot == null) panelRoot = transform.parent as RectTransform ?? GetComponent<RectTransform>();
        var canvas = panelRoot.GetComponentInParent<Canvas>();
        if (canvas != null) _canvasRect = canvas.transform as RectTransform;
        TryRestorePosition();
    }

    /// <summary>
    /// Indique à Unity qu'on accepte le drag sans seuil (sinon, un IBeginDragHandler
    /// frère pourrait gagner). Sans cette implémentation explicite, le drag s'amorce
    /// quand même mais ce hook clarifie le contrat.
    /// </summary>
    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (panelRoot == null) return;
        var parentRect = panelRoot.parent as RectTransform;
        if (parentRect == null) return;
        // Conversion screen-space → local au parent du panneau, puis offset par rapport
        // au pivot courant. On stocke cet offset pour le préserver pendant le drag.
        Vector2 localPointer;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect, eventData.position, eventData.pressEventCamera, out localPointer);
        _pointerOffsetLocal = localPointer - panelRoot.anchoredPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (panelRoot == null) return;
        var parentRect = panelRoot.parent as RectTransform;
        if (parentRect == null) return;
        Vector2 localPointer;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out localPointer))
            return;
        Vector2 target = localPointer - _pointerOffsetLocal;
        if (clampToCanvas) target = ClampToCanvas(target);
        panelRoot.anchoredPosition = target;
    }

    private void OnDisable() {
        TryPersistPosition();
    }

    private Vector2 ClampToCanvas(Vector2 candidate)
    {
        if (_canvasRect == null) return candidate;
        var parentRect = panelRoot.parent as RectTransform;
        if (parentRect == null) return candidate;

        // Canvas world bounds.
        Vector3[] canvasCorners = new Vector3[4];
        _canvasRect.GetWorldCorners(canvasCorners);
        float canvasMinX = canvasCorners[0].x;
        float canvasMaxX = canvasCorners[2].x;
        float canvasMinY = canvasCorners[0].y;
        float canvasMaxY = canvasCorners[2].y;

        // Centre monde et half-extents du header. On part toujours du CENTRE (calculé
        // via GetWorldCorners) plutôt que de transform.position qui retourne la position
        // du pivot — ces deux valeurs diffèrent si le pivot n'est pas au centre du rect,
        // et c'est exactement ce qui bloquait le drag à mi-écran.
        Vector3[] headerCorners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(headerCorners);
        Vector3 headerCenter = (headerCorners[0] + headerCorners[2]) * 0.5f;
        float headerHalfW = (headerCorners[2].x - headerCorners[0].x) * 0.5f;
        float headerHalfH = (headerCorners[2].y - headerCorners[0].y) * 0.5f;

        // Plage autorisée pour le centre du header en monde.
        float minX = canvasMinX + headerHalfW;
        float maxX = canvasMaxX - headerHalfW;
        float minY = canvasMinY + headerHalfH;
        float maxY = canvasMaxY - headerHalfH;

        // Approche par DELTA : on n'essaie pas de convertir directement anchoredPosition
        // ↔ world (ça dépendrait des anchorMin/Max/pivot du panel, source du bug initial).
        // À la place : on calcule de combien la candidate fait bouger le panel par rapport
        // à sa position actuelle, on applique le même delta au header center, on clamp,
        // puis on traduit le delta clampé en anchoredPosition.
        Vector2 currentAnchored = panelRoot.anchoredPosition;
        Vector2 deltaAnchored = candidate - currentAnchored;
        Vector3 deltaWorld = parentRect.TransformVector(new Vector3(deltaAnchored.x, deltaAnchored.y, 0f));

        Vector3 newHeaderCenter = headerCenter + deltaWorld;
        newHeaderCenter.x = Mathf.Clamp(newHeaderCenter.x, minX, maxX);
        newHeaderCenter.y = Mathf.Clamp(newHeaderCenter.y, minY, maxY);

        Vector3 clampedDeltaWorld = newHeaderCenter - headerCenter;
        Vector3 clampedDeltaLocal = parentRect.InverseTransformVector(clampedDeltaWorld);
        return currentAnchored + new Vector2(clampedDeltaLocal.x, clampedDeltaLocal.y);
    }

    private void TryPersistPosition() {
        if (string.IsNullOrEmpty(persistKey) || panelRoot == null) return;
        PlayerPrefs.SetFloat(persistKey + "_x", panelRoot.anchoredPosition.x);
        PlayerPrefs.SetFloat(persistKey + "_y", panelRoot.anchoredPosition.y);
    }

    private void TryRestorePosition() {
        if (string.IsNullOrEmpty(persistKey) || panelRoot == null) return;
        if (!PlayerPrefs.HasKey(persistKey + "_x")) return;
        float x = PlayerPrefs.GetFloat(persistKey + "_x");
        float y = PlayerPrefs.GetFloat(persistKey + "_y");
        panelRoot.anchoredPosition = new Vector2(x, y);
    }
}
