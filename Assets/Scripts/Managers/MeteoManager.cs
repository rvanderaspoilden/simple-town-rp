using PolyverseSkiesAsset;
using UnityEngine;

/// <summary>
/// Drives the shared day/night cycle: rotates the sun/moon rig, blends the
/// PolyverseSkies skybox between its day and night materials, and fades the two
/// directional lights so the Sun Light reaches 0 intensity at night while the Moon
/// Light takes over. Time of day comes from the shared <see cref="TimeManager"/>.
/// </summary>
public class MeteoManager : MonoBehaviour {
    [SerializeField]
    private PolyverseSkies _polyverseSkies;

    [Header("Directional lights")]
    [SerializeField]
    private Light _sunLight;
    [SerializeField]
    private Light _moonLight;

    [Header("Max intensities (daylight sun / night moon)")]
    [SerializeField]
    private float _maxSunIntensity = 1.87f;
    [SerializeField]
    private float _maxMoonIntensity = 0.15f;

    [Header("Day / night window (hours, 0-24)")]
    [Tooltip("Sun starts rising — full night before this hour.")]
    [SerializeField]
    private float _dawnStart = 5f;
    [Tooltip("Full daylight reached at this hour.")]
    [SerializeField]
    private float _dawnEnd = 7f;
    [Tooltip("Sun starts setting — full day before this hour.")]
    [SerializeField]
    private float _duskStart = 18f;
    [Tooltip("Full night reached at this hour — Sun Light = 0 after this.")]
    [SerializeField]
    private float _duskEnd = 20f;

    private const float POLYVERSE_HOUR_CONSTANT = 1f / 12f;
    private const float ROTATION_HOUR_CONSTANT = 360f / 24f;

    private void FixedUpdate() {
        float hour = (float)(TimeManager.CurrentTime.TotalHours % 24.0);
        if (hour < 0f) {
            hour += 24f;
        }

        this.UpdateSkybox(hour);
        this.ManageRotation(hour);
        this.UpdateLightIntensities(hour);
    }

    private void UpdateSkybox(float hour) {
        if (this._polyverseSkies == null) {
            return;
        }

        float value = hour < 12f
            ? 1f - (hour * POLYVERSE_HOUR_CONSTANT)
            : (hour - 12f) * POLYVERSE_HOUR_CONSTANT;

        this._polyverseSkies.timeOfDay = Mathf.Clamp01(value);
    }

    private void ManageRotation(float hour) {
        this.transform.rotation = Quaternion.Euler((hour * ROTATION_HOUR_CONSTANT) + 180f, 0f, 0f);
    }

    private void UpdateLightIntensities(float hour) {
        float dayFactor = this.DayFactor(hour); // 1 = full day, 0 = full night

        if (this._sunLight != null) {
            this._sunLight.intensity = this._maxSunIntensity * dayFactor;
        }

        if (this._moonLight != null) {
            this._moonLight.intensity = this._maxMoonIntensity * (1f - dayFactor);
        }
    }

    /// <summary>
    /// Returns 0 during the night, 1 during the day, linearly ramped across the
    /// dawn (<see cref="_dawnStart"/>→<see cref="_dawnEnd"/>) and dusk
    /// (<see cref="_duskStart"/>→<see cref="_duskEnd"/>) windows.
    /// </summary>
    private float DayFactor(float hour) {
        if (hour <= this._dawnStart || hour >= this._duskEnd) {
            return 0f;
        }

        if (hour >= this._dawnEnd && hour <= this._duskStart) {
            return 1f;
        }

        if (hour < this._dawnEnd) {
            return Mathf.InverseLerp(this._dawnStart, this._dawnEnd, hour);
        }

        return Mathf.InverseLerp(this._duskEnd, this._duskStart, hour);
    }
}
