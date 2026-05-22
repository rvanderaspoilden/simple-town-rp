using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Swaps the object's renderers to the "MissionHighlight" layer while active.
    /// Used by MissionHighlightManager to highlight current mission targets.
    /// </summary>
    public class MissionHighlightEffect : MonoBehaviour {
        public const string LayerName = "MissionHighlight";

        private Renderer[] _renderers;
        private int[]      _originalLayers;
        private bool       _shown;

        public bool IsHighlighted => _shown;

        public void Show() {
            if (_shown) return;

            var hover = GetComponent<PropHoverOutline>();
            if (hover != null) hover.Hide();

            int layer = LayerMask.NameToLayer(LayerName);
            if (layer < 0) return;

            _renderers = GetComponentsInChildren<Renderer>();
            _originalLayers = new int[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) {
                GameObject go = _renderers[i].gameObject;
                _originalLayers[i] = go.layer;
                go.layer = layer;
            }
            _shown = true;
        }

        public void Hide() {
            if (!_shown) return;
            if (_renderers != null) {
                for (int i = 0; i < _renderers.Length; i++) {
                    if (_renderers[i] != null) _renderers[i].gameObject.layer = _originalLayers[i];
                }
            }
            _renderers = null;
            _originalLayers = null;
            _shown = false;
        }

        private void OnDisable() => Hide();
    }
}
