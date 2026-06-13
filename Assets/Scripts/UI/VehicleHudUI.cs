using TMPro;
using UnityEngine;

namespace Sim.UI {
    /// <summary>
    /// Panneau HUD affiché pendant la conduite : vitesse en km/h, nom du modèle et rappel des
    /// touches. Le rappel des touches est du texte statique autoré dans le prefab HUD ; ce
    /// composant ne pilote que la vitesse + le modèle et l'affichage on/off.
    ///
    /// Activé par <see cref="HUDManager.ShowVehicleHud"/> à l'entrée en conduite
    /// (<c>CharacterDrive.OnEnter</c>) et masqué à la sortie.
    /// </summary>
    public class VehicleHudUI : MonoBehaviour {
        [Header("Settings")]
        [Tooltip("Texte de la vitesse, mis à jour chaque frame (« 42 km/h »).")]
        [SerializeField] private TextMeshProUGUI speedText;
        [Tooltip("Nom du modèle conduit (optionnel).")]
        [SerializeField] private TextMeshProUGUI modelText;

        private VehicleController vehicle;

        public void Show(VehicleController target) {
            this.vehicle = target;
            if (this.modelText != null) {
                this.modelText.text = target != null && target.Config != null ? target.Config.modelName : string.Empty;
            }
            this.gameObject.SetActive(true);
        }

        public void Hide() {
            this.vehicle = null;
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.vehicle == null || this.speedText == null) return;
            this.speedText.text = $"{this.vehicle.SpeedKmh:0} km/h";
        }
    }
}
