using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Etat runtime d'un objet à emballer. Pre-cache les 4 rotations pour éviter
    /// toute allocation pendant le drag.
    /// </summary>
    public class PackageItemInstance {
        public int Id { get; }
        public PackageItemDefinition Definition { get; }
        public Vector2Int Origin   { get; private set; }
        public int Rotation        { get; private set; }
        public bool IsPlaced       { get; private set; }

        private readonly PackageShape[] _rotations = new PackageShape[4];

        public PackageItemInstance(int id, PackageItemDefinition def) {
            Id = id;
            Definition = def;
            _rotations[0] = def.shape;
            for (int i = 1; i < 4; i++) {
                _rotations[i] = _rotations[i - 1].Rotated90CW();
            }
        }

        public PackageShape GetRotatedShape(int rotation) {
            int r = ((rotation % 4) + 4) % 4;
            return _rotations[r];
        }

        public void SetPlaced(Vector2Int origin, int rotation) {
            Origin = origin;
            Rotation = ((rotation % 4) + 4) % 4;
            IsPlaced = true;
        }

        public void SetUnplaced() {
            IsPlaced = false;
        }
    }
}
