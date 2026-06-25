using UnityEngine;

namespace Sim {
    /// <summary>
    /// Shrinks a ceiling light's range to the room it sits in so the light volume stays
    /// contained within floor + walls instead of bleeding into the apartment below or the
    /// neighbouring room. Casts rays against the room shell (Ground + Wall + Roof) along
    /// the spot axis and around a horizontal ring, then clamps the light range to the
    /// nearest surface (never above the range authored on the prefab).
    ///
    /// Bulbs are player-placeable props, so the fit is recomputed whenever the light moves
    /// (placement / edit). Furniture and other props are ignored — only the room shell
    /// constrains the range.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class BulbLightRangeFitter : MonoBehaviour {
        [Tooltip("Layers treated as the room shell (floor / walls / ceiling). Empty = Ground+Wall+Roof.")]
        [SerializeField] private LayerMask shellMask = 0;

        [Tooltip("Extra distance kept past the nearest surface so the falloff reaches it.")]
        [SerializeField] private float margin = 0.3f;

        [Tooltip("Range never drops below this, even pressed against a wall.")]
        [SerializeField] private float minRange = 1.5f;

        [Tooltip("Horizontal rays used to detect surrounding walls.")]
        [SerializeField] private int horizontalRays = 8;

        [Tooltip("Dim the bulb in proportion to the fitted range so a small room doesn't get a blown-out hot pool.")]
        [SerializeField] private bool scaleIntensityWithRange = true;

        [Tooltip("Lowest fraction of the authored intensity, reached when the range is clamped to minRange.")]
        [Range(0f, 1f)]
        [SerializeField] private float minIntensityFactor = 0.4f;

        private Light _light;
        private float _authoredRange;
        private float _authoredIntensity;
        private Vector3 _lastPos;
        private Quaternion _lastRot;
        private bool _hasShell;

        private void Awake() {
            _light = GetComponent<Light>();
            _authoredRange = _light.range;
            _authoredIntensity = _light.intensity;
            if (shellMask.value == 0) shellMask = LayerMask.GetMask("Ground", "Wall", "Roof");
        }

        private void OnEnable() => Fit();

        private void Update() {
            if (transform.position != _lastPos || transform.rotation != _lastRot) Fit();
        }

        /// <summary>Recomputes <c>Light.range</c> from the surrounding room shell.</summary>
        public void Fit() {
            _lastPos = transform.position;
            _lastRot = transform.rotation;

            Vector3 origin = transform.position;
            float nearest = _authoredRange;

            // Spot axis (the prefab aims the light straight down via a +90°X rotation).
            nearest = Mathf.Min(nearest, CastDist(origin, transform.forward));

            // Horizontal ring to catch the walls the wide cone spills onto.
            int n = Mathf.Max(0, horizontalRays);
            for (int i = 0; i < n; i++) {
                float a = (i / (float)n) * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                nearest = Mathf.Min(nearest, CastDist(origin, dir));
            }

            _light.range = Mathf.Clamp(nearest + margin, minRange, _authoredRange);

            // Smaller room -> shorter range -> steeper falloff, so trim intensity in step to
            // keep the pool gentle instead of blowing out under a low ceiling.
            if (scaleIntensityWithRange) {
                float t = Mathf.InverseLerp(minRange, _authoredRange, _light.range);
                _light.intensity = _authoredIntensity * Mathf.Lerp(minIntensityFactor, 1f, t);
            }
        }

        private float CastDist(Vector3 origin, Vector3 dir) {
            return Physics.Raycast(origin, dir, out RaycastHit hit, _authoredRange, shellMask, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : _authoredRange;
        }
    }
}
