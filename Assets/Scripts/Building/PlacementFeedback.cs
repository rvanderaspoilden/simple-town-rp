using System.Collections.Generic;
using UnityEngine;

namespace Sim.Building {
    /// <summary>
    /// Validity feedback for a previewed object (held-item ghost OR build-mode prop): a coloured
    /// outline around the object — green when placeable, red when blocked — plus a floating
    /// pastille badge above it (<see cref="PlacementBillboard"/>). Replaces the old green/red
    /// renderer tint. Added to the previewed object by <see cref="BuildPreview"/> and driven via
    /// <see cref="SetValid"/>; item and prop placement share this single feedback path.
    ///
    /// The outline reuses the project's layer-swap system (same as HoverOutline /
    /// MissionHighlightEffect): the object's mesh renderers are moved onto the "Place Valid" /
    /// "Place Invalid" layers, which the URP OutlineRendererFeature draws as a clean screen-space
    /// silhouette (one feature per layer/colour). CameraManager keeps both layers in the culling
    /// mask (WithOutlineLayer) so the mesh itself stays visible while outlined.
    /// </summary>
    public class PlacementFeedback : MonoBehaviour {
        private const string ValidLayerName   = "Place Valid";
        private const string InvalidLayerName = "Place Invalid";
        private const float BadgeMargin = 0.55f; // metres above the object's top

        private GameObject[] _renderGos;
        private int[] _originalLayers;
        private int _validLayer   = -1;
        private int _invalidLayer = -1;

        private PlacementBillboard _billboard;
        private bool _cleared;

        private void Awake() {
            _validLayer   = LayerMask.NameToLayer(ValidLayerName);
            _invalidLayer = LayerMask.NameToLayer(InvalidLayerName);

            // Only MeshRenderer / SkinnedMeshRenderer drive the outline (same filter as
            // MissionHighlightEffect) — sprites, particles, etc. have no silhouette.
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            var gos    = new List<GameObject>(renderers.Length);
            var layers = new List<int>(renderers.Length);
            float topY = this.transform.position.y;
            bool hasBounds = false;
            foreach (Renderer r in renderers) {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                gos.Add(r.gameObject);
                layers.Add(r.gameObject.layer);
                Bounds b = r.bounds;
                topY = hasBounds ? Mathf.Max(topY, b.max.y) : b.max.y;
                hasBounds = true;
            }
            _renderGos      = gos.ToArray();
            _originalLayers = layers.ToArray();

            float height = (hasBounds ? topY - this.transform.position.y : 0.3f) + BadgeMargin;
            _billboard = PlacementBillboard.Create(this.transform, height);
        }

        /// <summary>Updates both the outline colour (layer swap) and the badge to reflect validity.</summary>
        public void SetValid(bool valid) {
            if (_cleared) return;
            int layer = valid ? _validLayer : _invalidLayer;
            if (layer >= 0 && _renderGos != null) {
                foreach (GameObject go in _renderGos) if (go != null) go.layer = layer;
            }
            if (_billboard != null) _billboard.SetValid(valid);
        }

        /// <summary>Restores the original renderer layers and removes the badge, then self-destructs.
        /// Call before the preview ends (from BuildPreview.Destroy) so the object returns to its
        /// normal layer with no outline.</summary>
        public void Clear() {
            if (_cleared) return;
            _cleared = true;
            RestoreLayers();
            DisposeBillboard();
            Destroy(this);
        }

        private void RestoreLayers() {
            if (_renderGos == null) return;
            for (int i = 0; i < _renderGos.Length; i++) {
                if (_renderGos[i] != null) _renderGos[i].layer = _originalLayers[i];
            }
        }

        private void DisposeBillboard() {
            if (_billboard != null) { _billboard.Dispose(); _billboard = null; }
        }

        private void OnDestroy() {
            // Safety net: if the host object is destroyed without Clear() (e.g. ghost discarded),
            // still restore layers and remove the stray badge.
            if (!_cleared) {
                RestoreLayers();
                DisposeBillboard();
            }
        }
    }
}
