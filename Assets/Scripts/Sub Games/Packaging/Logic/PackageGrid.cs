using System.Collections.Generic;
using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Modèle pur de la grille du colis. Aucune dépendance Unity au runtime
    /// (sauf Vector2Int). Toute la logique view/input passe par cette classe.
    /// </summary>
    public class PackageGrid {
        public int Width  { get; }
        public int Height { get; }

        // -1 = empty, otherwise PackageItemInstance.Id
        private readonly int[,] _cells;
        private readonly Dictionary<int, PackageItemInstance> _items = new Dictionary<int, PackageItemInstance>();

        public PackageGrid(int width, int height) {
            Width = width;
            Height = height;
            _cells = new int[width, height];
            Clear();
        }

        public IReadOnlyDictionary<int, PackageItemInstance> Items => _items;

        public bool IsInside(Vector2Int p) =>
            p.x >= 0 && p.y >= 0 && p.x < Width && p.y < Height;

        public int CellOwner(Vector2Int p) => _cells[p.x, p.y];

        public void Clear() {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _cells[x, y] = -1;
            _items.Clear();
        }

        public bool CanPlace(PackageItemInstance item, Vector2Int origin, int rotation) {
            var shape = item.GetRotatedShape(rotation);
            if (shape.cells == null || shape.cells.Length == 0) return false;

            for (int i = 0; i < shape.cells.Length; i++) {
                var p = origin + shape.cells[i];
                if (!IsInside(p)) return false;
                int owner = _cells[p.x, p.y];
                if (owner != -1 && owner != item.Id) return false;
            }
            return true;
        }

        public bool Place(PackageItemInstance item, Vector2Int origin, int rotation) {
            if (!CanPlace(item, origin, rotation)) return false;
            if (item.IsPlaced) Remove(item);

            var shape = item.GetRotatedShape(rotation);
            for (int i = 0; i < shape.cells.Length; i++) {
                var c = shape.cells[i];
                _cells[origin.x + c.x, origin.y + c.y] = item.Id;
            }
            item.SetPlaced(origin, rotation);
            _items[item.Id] = item;
            return true;
        }

        public void Remove(PackageItemInstance item) {
            if (!item.IsPlaced) return;
            var shape = item.GetRotatedShape(item.Rotation);
            for (int i = 0; i < shape.cells.Length; i++) {
                var p = item.Origin + shape.cells[i];
                if (IsInside(p) && _cells[p.x, p.y] == item.Id) {
                    _cells[p.x, p.y] = -1;
                }
            }
            _items.Remove(item.Id);
            item.SetUnplaced();
        }

        public int OccupiedCells() {
            int n = 0;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (_cells[x, y] != -1) n++;
            return n;
        }
    }
}
