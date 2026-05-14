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

        public float CellSize => cellSize;
        public RectTransform ItemsContainer => itemsContainer;

        public void Build(PackageGrid grid, Camera uiCamera = null) {
            _grid = grid;
            _uiCamera = uiCamera;

            if (_cellViews != null) {
                for (int x = 0; x < _cellViews.GetLength(0); x++)
                    for (int y = 0; y < _cellViews.GetLength(1); y++)
                        if (_cellViews[x, y] != null) Destroy(_cellViews[x, y].gameObject);
            }

            _cellViews = new Image[grid.Width, grid.Height];
            cellsContainer.sizeDelta = new Vector2(grid.Width * cellSize, grid.Height * cellSize);

            for (int y = 0; y < grid.Height; y++) {
                for (int x = 0; x < grid.Width; x++) {
                    var img = Instantiate(cellPrefab, cellsContainer);
                    img.color = emptyColor;
                    var r = img.rectTransform;
                    r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
                    r.pivot = new Vector2(0f, 0f);
                    r.sizeDelta = new Vector2(cellSize, cellSize);
                    r.anchoredPosition = CellToLocal(new Vector2Int(x, y));
                    _cellViews[x, y] = img;
                }
            }
        }

        public Vector2 CellToLocal(Vector2Int cell) {
            return new Vector2(cell.x * cellSize, cell.y * cellSize);
        }

        /// <summary>
        /// Convertit position écran -> cellule de la grille (en cell-space).
        /// Renvoie un Vector2Int qui peut être hors grille — le caller doit IsInside().
        /// </summary>
        public Vector2Int ScreenToCell(Vector2 screenPos) {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                cellsContainer, screenPos, _uiCamera, out local);
            int cx = Mathf.FloorToInt(local.x / cellSize);
            int cy = Mathf.FloorToInt(local.y / cellSize);
            return new Vector2Int(cx, cy);
        }

        public void PlaceItemView(PackageItemView view, Vector2Int origin) {
            view.Rect.SetParent(itemsContainer, false);
            view.Rect.anchorMin = view.Rect.anchorMax = new Vector2(0f, 0f);
            view.Rect.pivot = new Vector2(0f, 0f);
            view.Rect.anchoredPosition = CellToLocal(origin);
        }
    }
}
