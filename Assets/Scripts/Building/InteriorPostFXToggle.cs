using UnityEngine;
using UnityEngine.Rendering;

namespace Sim.Building {
    /// <summary>
    /// Blends an interior-only post-processing <see cref="Volume"/> in while the local player
    /// is inside a room and back out in the city. The scene's base Global Volume stays at full
    /// weight everywhere; this one sits at a higher priority and only contributes inside, layering
    /// extra warmth / vignette / bloom on top for the cozy diorama look. The weight is eased over
    /// a short blend so the look doesn't pop at the door.
    ///
    /// Local-only (post FX is client-side) and mirrors <see cref="InteriorAmbientToggle"/> /
    /// <see cref="ExteriorLightToggle"/>, which drive ambient and the sun off the same room event.
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public class InteriorPostFXToggle : MonoBehaviour {
        [Tooltip("Seconds to ease between the city (weight 0) and interior (weight 1) looks.")]
        [SerializeField] private float blendDuration = 0.5f;

        private Volume _volume;
        private float _target;

        private void Awake() {
            _volume = GetComponent<Volume>();
            _volume.weight = 0f; // start on the city look until we hear otherwise
        }

        private void OnEnable() {
            ClientPropManager.OnLocalRoomChanged += OnLocalRoomChanged;
        }

        private void OnDisable() {
            ClientPropManager.OnLocalRoomChanged -= OnLocalRoomChanged;
            _target = 0f;
            if (_volume != null) _volume.weight = 0f;
        }

        private void OnLocalRoomChanged(string roomId) {
            _target = roomId == "city" ? 0f : 1f;
        }

        private void Update() {
            if (Mathf.Approximately(_volume.weight, _target)) return;
            float step = blendDuration > 0f ? Time.deltaTime / blendDuration : 1f;
            _volume.weight = Mathf.MoveTowards(_volume.weight, _target, step);
        }
    }
}
