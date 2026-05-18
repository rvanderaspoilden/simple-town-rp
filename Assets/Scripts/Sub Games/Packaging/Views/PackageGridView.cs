using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Affiche la grille de cellules et fournit la conversion screen <-> cell.
    /// Reste read-only sur la grille logique : ne mute jamais PackageGrid.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PackageGridView : MonoBehaviour {
        [Header("Refs")]
        [SerializeField] private RectTransform cellsContainer;
        [SerializeField] private RectTransform itemsContainer;
        [SerializeField] private Image cellPrefab;

        [Header("Layout")]
        [SerializeField] private float cellSize = 96f;
        [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.04f);

        private PackageGrid _grid;
        private Image[,] _cellViews;
        private Camera _uiCamera;
        private float _effectiveCellSize;
        private Vector2 _cellsOffset;

        public float CellSize => _effectiveCellSize > 0f ? _effectiveCellSize : cellSize;
        public Vector2 CellsOffset => _cellsOffset;
        public RectTransform ItemsContainer => itemsContainer;

        public void Build(PackageGrid grid, Camera uiCamera = null) {
            _grid = grid;
            _uiCamera = uiCamera;

            Canvas.ForceUpdateCanvases();
            var viewRect = ((RectTransform)transform).rect;
            _effectiveCellSize = Mathf.Floor(Mathf.Min(viewRect.width / grid.Width, viewRect.height / grid.Height));
            _effectiveCellSize = Mathf.Min(_effectiveCellSize, cellSize);
            _cellsOffset = new Vector2(
                (viewRect.width  - grid.Width  * _effectiveCellSize) * 0.5f,
                (viewRect.height - grid.Height * _effectiveCellSize) * 0.5f);

            cellsContainer.anchoredPosition = _cellsOffset;
            itemsContainer.anchoredPosition = _cellsOffset;

            if (_cellViews != null) {
                for (int x = 0; x < _cellViews.GetLength(0); x++)
                    for (int y = 0; y < _cellViews.GetLength(1); y++)
                        if (_cellViews[x, y] != null) Destroy(_cellViews[x, y].gameObject);
            }

            _cellViews = new Image[grid.Width, grid.Height];
            cellsContainer.sizeDelta = new Vector2(grid.Width * CellSize, grid.Height * CellSize);

            for (int y = 0; y < grid.Height; y++) {
                for (int x = 0; x < grid.Width; x++) {
                    var img = Instantiate(cellPrefab, cellsContainer);
                    img.color = emptyColor;
                    var r = img.rectTransform;
                    r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
                    r.pivot = new Vector2(0f, 0f);
                    r.sizeDelta = new Vector2(CellSize, CellSize);
                    r.anchoredPosition = CellToLocal(new Vector2Int(x, y));
                    _cellViews[x, y] = img;
                }
            }
        }

        public Vector2 CellToLocal(Vector2Int cell) {
            return new Vector2(cell.x * CellSize, cell.y * CellSize);
        }

        /// <summary>
        /// Convertit position écran -> cellule de la grille (en cell-space).
        /// Renvoie un Vector2Int qui peut être hors grille — le caller doit IsInside().
        /// </summary>
        public Vector2Int ScreenToCell(Vector2 screenPos) {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                cellsContainer, screenPos, _uiCamera, out local);
            int cx = Mathf.FloorToInt(local.x / CellSize);
            int cy = Mathf.FloorToInt(local.y / CellSize);
            return new Vector2Int(cx, cy);
        }

        /// <summary>
        /// Convertit position écran -> coordonnées locales dans l'espace de cellsContainer,
        /// sans clamper à la grille. Utilisé pour positionner le ghost hors grille dans le
        /// même système de coordonnées que CellToLocal.
        /// </summary>
        public Vector2 ScreenToLocalUnclamped(Vector2 screenPos) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                cellsContainer, screenPos, _uiCamera, out var local);
            return local;
        }

        public void PlaceItemView(PackageItemView view, Vector2Int origin) {
            view.Rect.SetParent(itemsContainer, false);
            view.Rect.anchorMin = view.Rect.anchorMax = new Vector2(0f, 0f);
            view.Rect.pivot = new Vector2(0f, 0f);
            view.Rect.anchoredPosition = CellToLocal(origin);
        }
    }
}
