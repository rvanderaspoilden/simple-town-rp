using System.Collections.Generic;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Swaps the object's mesh renderers to the "MissionHighlight" layer while active.
    /// Piloté soit par MissionHighlightManager (props de carrière, selon le step),
    /// soit directement par MissionItemBehaviour (colis : visible tant qu'au sol).
    /// </summary>
    public class MissionHighlightEffect : MonoBehaviour {
        public const string LayerName = "MissionHighlight";

        private Renderer[] _renderers;
        private int[]      _originalLayers;
        private bool       _shown;

        public bool IsHighlighted => _shown;

        public void Show() {
            if (_shown) return;

            var hover = GetComponent<HoverOutline>();
            if (hover != null) hover.Hide();

            int layer = LayerMask.NameToLayer(LayerName);
            if (layer < 0) return;

            // On ne surligne que les meshes (MeshRenderer / SkinnedMeshRenderer).
            // Les ParticleSystemRenderer, TrailRenderer, LineRenderer… ne doivent
            // jamais passer sur la couche outline (sinon les particules d'un éventuel
            // halo se retrouvent contourées, ce qui est moche).
            var all = GetComponentsInChildren<Renderer>();
            var meshes = new List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++) {
                if (all[i] is MeshRenderer || all[i] is SkinnedMeshRenderer) meshes.Add(all[i]);
            }

            _renderers = meshes.ToArray();
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
