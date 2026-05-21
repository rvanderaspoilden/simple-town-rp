using UnityEngine;

/// <summary>
/// Hover outline driver: moves the prop's renderer GameObjects onto the "Outline"
/// layer while hovered so the OutlineRendererFeature draws a screen-space silhouette
/// around them, and restores their original layers when the hover ends.
///
/// Added/driven at runtime by CameraManager's hover raycast — no per-prop setup.
/// Requires a layer named "Outline" (Project Settings ▸ Tags and Layers) + the
/// OutlineRendererFeature on the active URP Renderer.
/// </summary>
public class PropHoverOutline : MonoBehaviour {
    public const string OutlineLayerName = "Outline";

    private Renderer[] _renderers;
    private int[]      _originalLayers;
    private bool       _shown;

    public void Show() {
        if (_shown) return;
        int layer = LayerMask.NameToLayer(OutlineLayerName);
        if (layer < 0) return; // layer not created yet — no-op

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
