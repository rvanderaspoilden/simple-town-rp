using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Adds a hover outline to a prop by appending a shared inverted-hull outline material
/// (Custom/PropHoverOutline) to each of its renderers while hovered, and removing it
/// when the hover ends. Added/driven at runtime by CameraManager's hover raycast — no
/// per-prop setup required.
///
/// Uses sharedMaterials (not materials) so the prop's real materials aren't instanced.
/// </summary>
public class PropHoverOutline : MonoBehaviour {
    private static Material _outlineMaterial;

    private Renderer[]   _renderers;
    private Material[][]  _originalMaterials;
    private bool          _shown;

    public void Show() {
        if (_shown) return;
        Material outline = GetOutlineMaterial();
        if (outline == null) return;

        _renderers = GetComponentsInChildren<Renderer>();
        _originalMaterials = new Material[_renderers.Length][];

        for (int i = 0; i < _renderers.Length; i++) {
            Renderer r = _renderers[i];
            // Skip renderers that can't show an outline meaningfully (UI, particles…).
            if (r is MeshRenderer == false && r is SkinnedMeshRenderer == false) {
                _originalMaterials[i] = null;
                continue;
            }
            Material[] original = r.sharedMaterials;
            _originalMaterials[i] = original;

            Material[] withOutline = new Material[original.Length + 1];
            original.CopyTo(withOutline, 0);
            withOutline[original.Length] = outline;
            r.sharedMaterials = withOutline;
        }
        _shown = true;
    }

    public void Hide() {
        if (!_shown) return;
        if (_renderers != null) {
            for (int i = 0; i < _renderers.Length; i++) {
                if (_renderers[i] == null || _originalMaterials[i] == null) continue;
                _renderers[i].sharedMaterials = _originalMaterials[i];
            }
        }
        _renderers = null;
        _originalMaterials = null;
        _shown = false;
    }

    private void OnDisable() => Hide();

    private static Material GetOutlineMaterial() {
        if (_outlineMaterial == null) {
            Shader shader = Shader.Find("Custom/PropHoverOutline");
            if (shader != null) _outlineMaterial = new Material(shader) { name = "PropHoverOutline (Runtime)" };
        }
        return _outlineMaterial;
    }
}
