using UnityEngine;

namespace Sim.Building {
    /// <summary>
    /// World-space validity badge floating above a previewed object (held-item ghost or
    /// build-mode prop). A round pastille that always faces the camera: green check when the
    /// placement is valid, red cross when blocked. Created and destroyed at runtime by
    /// <see cref="PlacementFeedback"/> — no prefab to author (a single SpriteRenderer gizmo,
    /// which the "author UI in prefab" rule does not cover).
    /// </summary>
    public class PlacementBillboard : MonoBehaviour {
        private const string ValidSpritePath   = "Sprites/Placement/badge_valid";
        private const string InvalidSpritePath = "Sprites/Placement/badge_invalid";
        // 256px sprite at 100 PPU = 2.56 world units; 0.11 → ~0.28 m badge.
        private const float WorldScale = 0.11f;

        private static Sprite _validSprite;
        private static Sprite _invalidSprite;

        private SpriteRenderer _sprite;
        private Transform _target;
        private float _height;
        private Camera _cam;

        public static PlacementBillboard Create(Transform target, float height) {
            var go = new GameObject("PlacementBillboard");
            var billboard = go.AddComponent<PlacementBillboard>();
            billboard.Init(target, height);
            return billboard;
        }

        private void Init(Transform target, float height) {
            _target = target;
            _height = height;
            this.transform.localScale = Vector3.one * WorldScale;

            _sprite = this.gameObject.AddComponent<SpriteRenderer>();
            _sprite.sortingOrder = 32000; // draw above world geometry
            EnsureSprites();
            SetValid(false);
        }

        private static void EnsureSprites() {
            if (_validSprite == null)   _validSprite   = Resources.Load<Sprite>(ValidSpritePath);
            if (_invalidSprite == null) _invalidSprite = Resources.Load<Sprite>(InvalidSpritePath);
        }

        public void SetValid(bool valid) {
            if (_sprite == null) return;
            EnsureSprites();
            _sprite.sprite = valid ? _validSprite : _invalidSprite;
        }

        private void LateUpdate() {
            if (_target == null) { Dispose(); return; }
            this.transform.position = _target.position + Vector3.up * _height;
            if (_cam == null) _cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
            if (_cam != null) this.transform.rotation = _cam.transform.rotation;
        }

        public void Dispose() {
            if (this != null && this.gameObject != null) Destroy(this.gameObject);
        }
    }
}
