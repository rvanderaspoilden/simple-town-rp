using System;
using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Drag & drop souris + rotation au clavier (R). Lit la grille via
    /// PackageGrid pour valider le placement, déplace le ghost en cell-space,
    /// délègue toute logique d'occupation à PackageGrid.
    /// </summary>
    public class PackageInputController : MonoBehaviour {
        public event Action<PackageItemInstance> OnItemPlaced;
        public event Action<PackageItemInstance> OnItemReturned;

        [SerializeField] private KeyCode rotateKey = KeyCode.R;
        [SerializeField] private PackageGhostView ghost;

        private PackageGrid _grid;
        private PackageGridView _gridView;
        private PackageItemTrayView _trayView;
        private PackagingFeedback _feedback;
        private Camera _uiCamera;

        private PackageItemInstance _dragged;
        private PackageItemView _draggedView;
        private int _dragRotation;
        private Vector2Int _hoverCell;
        private bool _hoverValid;
        private bool _draggedWasPlaced;
        private Vector2Int _draggedOriginalOrigin;
        private int _draggedOriginalRotation;

        public bool IsDragging => _dragged != null;

        public void Bind(PackageGrid grid, PackageGridView gridView,
                         PackageItemTrayView trayView, PackagingFeedback feedback,
                         Camera uiCamera) {
            _grid = grid;
            _gridView = gridView;
            _trayView = trayView;
            _feedback = feedback;
            _uiCamera = uiCamera;
            if (ghost != null) ghost.Hide();
        }

        public void Unbind() {
            CancelDrag();
            _grid = null;
            _gridView = null;
            _trayView = null;
        }

        private void Update() {
            if (_grid == null) return;

            if (_dragged == null) {
                if (Input.GetMouseButtonDown(0)) TryBeginDrag();
                return;
            }

            if (Input.GetKeyDown(rotateKey) && _dragged.Definition.allowRotation) {
                _dragRotation = (_dragRotation + 1) % 4;
                if (_draggedView != null) _draggedView.SetRotationVisual(_dragRotation, _gridView.CellSize);
                if (ghost != null) ghost.Show(_dragged, _dragRotation, _gridView.CellSize);
                if (_feedback != null) _feedback.PlayRotateFeedback();
            }

            UpdateHover();

            if (Input.GetMouseButtonUp(0)) TryDrop();
        }

        private void TryBeginDrag() {
            // 1) tray
            var trayPick = _trayView != null ? _trayView.PickAt(Input.mousePosition, _uiCamera) : null;
            if (trayPick != null) {
                BeginDrag(trayPick, false, Vector2Int.zero, 0);
                return;
            }

            // 2) grid (relocate placed item)
            var cell = _gridView.ScreenToCell(Input.mousePosition);
            if (!_grid.IsInside(cell)) return;
            int owner = _grid.CellOwner(cell);
            if (owner == -1) return;
            if (!_grid.Items.TryGetValue(owner, out var inst)) return;
            // Need to find the view from the items container — we keep it via PackagingSubGameManager,
            // but for simplicity we also expose pick by id through the tray map (placed views live
            // under itemsContainer but we still track them via trayView's map).
            if (_trayView != null && _trayView.Views.TryGetValue(owner, out var placedView)) {
                BeginDrag(placedView, true, inst.Origin, inst.Rotation);
            }
        }

        private void BeginDrag(PackageItemView view, bool fromGrid, Vector2Int origOrigin, int origRotation) {
            _dragged = view.Instance;
            _draggedView = view;
            _dragRotation = fromGrid ? origRotation : 0;
            _draggedWasPlaced = fromGrid;
            _draggedOriginalOrigin = origOrigin;
            _draggedOriginalRotation = origRotation;

            if (fromGrid) {
                _grid.Remove(_dragged);
                OnItemReturned?.Invoke(_dragged);
            }

            view.SetAlpha(0f);
            view.SetInteractable(false);

            if (ghost != null) ghost.Show(_dragged, _dragRotation, _gridView.CellSize);
        }

        private void UpdateHover() {
            _hoverCell = _gridView.ScreenToCell(Input.mousePosition);
            _hoverValid = _grid.IsInside(_hoverCell) && _grid.CanPlace(_dragged, _hoverCell, _dragRotation);

            if (ghost != null) {
                ghost.gameObject.SetActive(true);
                if (_grid.IsInside(_hoverCell)) {
                    // Snap sur la cellule survolée, couleur valid/invalid.
                    ghost.UpdatePosition(_gridView.CellToLocal(_hoverCell), _hoverValid);
                } else {
                    // Hors grille : centré sur le curseur dans l'espace de la grille,
                    // même référentiel que CellToLocal → pas de décalage de pivot/ancrage.
                    var cursorLocal = _gridView.ScreenToLocalUnclamped(Input.mousePosition);
                    ghost.UpdatePosition(cursorLocal - ghost.Rect.sizeDelta * 0.5f, false);
                }
            }
        }

        private void TryDrop() {
            bool placed = _hoverValid && _grid.Place(_dragged, _hoverCell, _dragRotation);

            if (placed) {
                _draggedView.SetRotationVisual(_dragRotation, _gridView.CellSize);
                _gridView.PlaceItemView(_draggedView, _hoverCell);
                _draggedView.SetAlpha(1f);
                _draggedView.SetInteractable(true);
                OnItemPlaced?.Invoke(_dragged);
            } else {
                _feedback?.PlayRejectFeedback();
                if (_draggedWasPlaced) {
                    // Restore at original location
                    _grid.Place(_dragged, _draggedOriginalOrigin, _draggedOriginalRotation);
                    _draggedView.SetRotationVisual(_draggedOriginalRotation, _gridView.CellSize);
                    _gridView.PlaceItemView(_draggedView, _draggedOriginalOrigin);
                } else {
                    // Back to tray
                    if (_trayView != null) _trayView.RestoreView(_dragged.Id);
                }
                _draggedView.SetAlpha(1f);
                _draggedView.SetInteractable(true);
            }

            if (ghost != null) ghost.Hide();
            _dragged = null;
            _draggedView = null;
            _dragRotation = 0;
        }

        private void CancelDrag() {
            if (_dragged == null) return;
            if (_draggedView != null) {
                _draggedView.SetAlpha(1f);
                _draggedView.SetInteractable(true);
            }
            if (ghost != null) ghost.Hide();
            _dragged = null;
            _draggedView = null;
        }
    }
}
