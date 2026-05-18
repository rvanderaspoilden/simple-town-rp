using System;
using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Forme d'un objet exprimée en cellules de grille relatives à l'origine (0,0).
    /// Toujours normalisée : min(x,y) == 0.
    /// </summary>
    [Serializable]
    public struct PackageShape {
        public Vector2Int[] cells;

        public PackageShape(Vector2Int[] cells) {
            this.cells = cells ?? Array.Empty<Vector2Int>();
        }

        public PackageShape Rotated90CW() {
            if (cells == null || cells.Length == 0) return new PackageShape(Array.Empty<Vector2Int>());

            var result = new Vector2Int[cells.Length];
            int minX = int.MaxValue, minY = int.MaxValue;
            for (int i = 0; i < cells.Length; i++) {
                var r = new Vector2Int(cells[i].y, -cells[i].x);
                result[i] = r;
                if (r.x < minX) minX = r.x;
                if (r.y < minY) minY = r.y;
            }
            for (int i = 0; i < result.Length; i++) {
                result[i] = new Vector2Int(result[i].x - minX, result[i].y - minY);
            }
            return new PackageShape(result);
        }

        public RectInt Bounds() {
            if (cells == null || cells.Length == 0) return new RectInt(0, 0, 0, 0);
            int maxX = 0, maxY = 0;
            for (int i = 0; i < cells.Length; i++) {
                if (cells[i].x > maxX) maxX = cells[i].x;
                if (cells[i].y > maxY) maxY = cells[i].y;
            }
            return new RectInt(0, 0, maxX + 1, maxY + 1);
        }
    }
}
