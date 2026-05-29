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
        // Bornes : on garde au minimum la barre de titre (ce GameObject) à l'intérieur
        // du Canvas. Calcule les half-extents en monde, convertit en local au parent.
        var parentRect = panelRoot.parent as RectTransform;
        if (parentRect == null) return candidate;

        Vector3[] canvasCorners = new Vector3[4];
        _canvasRect.GetWorldCorners(canvasCorners);
        Vector3[] headerCorners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(headerCorners);

        // Décalage actuel entre la position monde du panel et celle du header :
        // tant que le header reste DANS le canvas, on est OK.
        Vector3 panelWorld = panelRoot.position;
        Vector3 headerWorld = transform.position;
        Vector3 headerToPanel = panelWorld - headerWorld;

        // Plages autorisées pour la position monde du HEADER (centre).
        float headerHalfW = (headerCorners[2].x - headerCorners[0].x) * 0.5f;
        float headerHalfH = (headerCorners[2].y - headerCorners[0].y) * 0.5f;
        float minX = canvasCorners[0].x + headerHalfW;
        float maxX = canvasCorners[2].x - headerHalfW;
        float minY = canvasCorners[0].y + headerHalfH;
        float maxY = canvasCorners[2].y - headerHalfH;

        // Convertit la candidate (locale au parent du panneau) en position monde,
        // clamp dans la zone autorisée, puis re-convertit en locale.
        Vector3 candidateLocal3 = new Vector3(candidate.x, candidate.y, panelRoot.localPosition.z);
        Vector3 candidateWorld = parentRect.TransformPoint(candidateLocal3);
        Vector3 candidateHeaderWorld = candidateWorld - headerToPanel;

        candidateHeaderWorld.x = Mathf.Clamp(candidateHeaderWorld.x, minX, maxX);
        candidateHeaderWorld.y = Mathf.Clamp(candidateHeaderWorld.y, minY, maxY);

        Vector3 clampedWorld = candidateHeaderWorld + headerToPanel;
        Vector3 clampedLocal = parentRect.InverseTransformPoint(clampedWorld);
        return new Vector2(clampedLocal.x, clampedLocal.y);
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
