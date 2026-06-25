using UnityEngine;
using UnityEngine.Rendering;

namespace Sim.Building {
    /// <summary>
    /// Darkens and cools the ambient light while the local player is inside an interior
    /// room so the warm bulb pools become the dominant light source (cozy / diorama look)
    /// instead of being washed out by the scene's bright skybox ambient. Restores the
    /// scene's authored ambient when back in the city.
    ///
    /// Local-only (RenderSettings is client-side) and mirrors <see cref="ExteriorLightToggle"/>,
    /// which kills the directional sun inside for the same reason.
    /// </summary>
    public class InteriorAmbientToggle : MonoBehaviour {
        [Tooltip("Flat ambient colour applied inside. Dark + cool to contrast the warm bulbs.")]
        [SerializeField] private Color interiorAmbient = new Color(0.09f, 0.10f, 0.16f, 1f);

        [Tooltip("Skybox reflection strength inside. Low kills the washed-out sheen on walls.")]
        [Range(0f, 1f)]
        [SerializeField] private float interiorReflectionIntensity = 0.15f;

        // Authored scene ambient, captured once and restored when leaving the interior.
        private AmbientMode _savedMode;
        private Color _savedFlat, _savedSky, _savedEquator, _savedGround;
        private float _savedIntensity;
        private float _savedReflection;
        private bool _captured;
        private bool _overriding;

        private void OnEnable() {
            ClientPropManager.OnLocalRoomChanged += OnLocalRoomChanged;
        }

        private void OnDisable() {
            ClientPropManager.OnLocalRoomChanged -= OnLocalRoomChanged;
            Restore();
        }

        private void OnLocalRoomChanged(string roomId) {
            if (roomId == "city") Restore();
            else ApplyInterior();
        }

        private void ApplyInterior() {
            if (!_captured) {
                _savedMode      = RenderSettings.ambientMode;
                _savedFlat      = RenderSettings.ambientLight;
                _savedSky       = RenderSettings.ambientSkyColor;
                _savedEquator   = RenderSettings.ambientEquatorColor;
                _savedGround    = RenderSettings.ambientGroundColor;
                _savedIntensity = RenderSettings.ambientIntensity;
                _savedReflection = RenderSettings.reflectionIntensity;
                _captured = true;
            }
            RenderSettings.ambientMode         = AmbientMode.Flat;
            RenderSettings.ambientLight        = interiorAmbient;
            RenderSettings.reflectionIntensity = interiorReflectionIntensity;
            _overriding = true;
        }

        private void Restore() {
            if (!_captured || !_overriding) return;
            RenderSettings.ambientMode         = _savedMode;
            RenderSettings.ambientLight        = _savedFlat;
            RenderSettings.ambientSkyColor     = _savedSky;
            RenderSettings.ambientEquatorColor = _savedEquator;
            RenderSettings.ambientGroundColor  = _savedGround;
            RenderSettings.ambientIntensity    = _savedIntensity;
            RenderSettings.reflectionIntensity = _savedReflection;
            _overriding = false;
        }
    }
}
