using System.Collections.Generic;
using UnityEngine;

namespace Sim.Building {
    /// <summary>
    /// Disables the city's directional light(s) on the local client while the local player
    /// is inside an interior room (hall / apartment, spawned in the void), then re-enables
    /// them in the city. Directional lights have infinite range, so otherwise the city sun
    /// floods the void interiors and casts incoherent exterior shadows.
    ///
    /// Lighting is rendered client-side, so this is purely local: toggling here does not
    /// affect how other players' clients render their own view.
    /// </summary>
    public class ExteriorLightToggle : MonoBehaviour {
        [Tooltip("Directional lights to toggle. Left empty = auto-find every directional light in the scene on Awake.")]
        [SerializeField] private Light[] exteriorLights;

        private void Awake() {
            if (this.exteriorLights == null || this.exteriorLights.Length == 0) {
                List<Light> found = new List<Light>();
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) {
                    if (l.type == LightType.Directional) {
                        found.Add(l);
                    }
                }
                this.exteriorLights = found.ToArray();
            }
        }

        private void OnEnable() {
            ClientPropManager.OnLocalRoomChanged += OnLocalRoomChanged;
        }

        private void OnDisable() {
            ClientPropManager.OnLocalRoomChanged -= OnLocalRoomChanged;
        }

        private void OnLocalRoomChanged(string roomId) {
            bool inCity = roomId == "city";
            for (int i = 0; i < this.exteriorLights.Length; i++) {
                if (this.exteriorLights[i] != null) {
                    this.exteriorLights[i].enabled = inCity;
                }
            }
        }
    }
}
