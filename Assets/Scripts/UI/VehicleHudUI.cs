using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Panneau HUD affiché aux occupants d'un véhicule (conducteur ET passagers) : vitesse km/h,
    /// modèle, BARRE DE VIE du véhicule, et rappel des touches (complet pour le conducteur, réduit
    /// à « sortir » pour les passagers).
    ///
    /// Activé par <see cref="HUDManager.ShowVehicleHud"/> (CharacterDrive / CharacterPassenger),
    /// masqué à la sortie.
    /// </summary>
    public class VehicleHudUI : MonoBehaviour {
        [Header("Settings")]
        [Tooltip("Texte de la vitesse, mis à jour chaque frame (« 42 km/h »).")]
        [SerializeField] private TextMeshProUGUI speedText;
        [Tooltip("Nom du modèle conduit (optionnel).")]
        [SerializeField] private TextMeshProUGUI modelText;
        [Tooltip("Image (Filled Horizontal) de la barre de vie du véhicule.")]
        [SerializeField] private Image healthFill;
        [Tooltip("Image (Filled Horizontal) de la jauge de carburant.")]
        [SerializeField] private Image fuelFill;
        [Tooltip("Texte des touches : complet pour le conducteur, réduit pour le passager.")]
        [SerializeField] private TextMeshProUGUI keysText;
        [Tooltip("Indicateur d'état verrouillé / déverrouillé.")]
        [SerializeField] private TextMeshProUGUI lockText;

        private const string DriverKeys    = "Z/S : avancer/reculer    Q/D : tourner    ESPACE : frein    F : phares    H : klaxon    L : verrouiller    X : sortir";
        private const string PassengerKeys = "X : descendre";

        private VehicleController vehicle;

        public void Show(VehicleController target, bool asDriver) {
            this.vehicle = target;
            if (this.modelText != null) {
                this.modelText.text = target != null && target.Config != null ? target.Config.modelName : string.Empty;
            }
            if (this.keysText != null) this.keysText.text = asDriver ? DriverKeys : PassengerKeys;
            this.gameObject.SetActive(true);
        }

        public void Hide() {
            this.vehicle = null;
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.vehicle == null) return;
            if (this.speedText != null)
                this.speedText.text = this.vehicle.IsKO ? "KO" : $"{this.vehicle.SpeedKmh:0} km/h";
            if (this.healthFill != null) {
                float t = this.vehicle.HealthNormalized;
                this.healthFill.fillAmount = t;
                this.healthFill.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.35f, 0.85f, 0.4f), t);
            }
            if (this.fuelFill != null) {
                float f = this.vehicle.FuelNormalized;
                this.fuelFill.fillAmount = f;
                // Vert plein → orange → rouge sous ~20 %.
                this.fuelFill.color = f <= 0.2f
                    ? Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.95f, 0.6f, 0.2f), f / 0.2f)
                    : Color.Lerp(new Color(0.95f, 0.6f, 0.2f), new Color(0.4f, 0.8f, 0.95f), (f - 0.2f) / 0.8f);
            }
            if (this.lockText != null) {
                bool locked = this.vehicle.IsLocked;
                this.lockText.text = locked ? "VERROUILLÉ" : "DÉVERROUILLÉ";
                this.lockText.color = locked ? new Color(0.95f, 0.5f, 0.3f) : new Color(0.6f, 0.85f, 0.6f);
            }
        }
    }
}
