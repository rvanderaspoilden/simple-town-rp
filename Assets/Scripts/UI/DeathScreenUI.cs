using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// HUD de MORT du joueur local : voile sombre plein écran, compte à rebours du respawn
    /// automatique et bouton « Réapparaître » (respawn immédiat, Command vers le serveur).
    /// Ouvert par PlayerController.TargetKill, fermé par TargetRevive. Tant qu'il est ouvert,
    /// le curseur est libéré (CursorLock.IsBlockingUiOpen) et la caméra surplombe le cadavre
    /// (TpsShoulderCamera, vue de mort). UI autorée dans le prefab HUD Manager.
    /// </summary>
    public class DeathScreenUI : MonoBehaviour {
        public static DeathScreenUI Instance { get; private set; }

        [Tooltip("Racine visuelle (voile + textes + bouton), inactive par défaut.")]
        [SerializeField] private GameObject root;
        [Tooltip("Texte du compte à rebours avant respawn automatique.")]
        [SerializeField] private TMP_Text countdownText;
        [Tooltip("Bouton de respawn immédiat.")]
        [SerializeField] private Button respawnButton;

        private float _deadline = -1f;
        private bool _requested;
        private int _lastShownSeconds = -1;

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (respawnButton != null) respawnButton.onClick.AddListener(OnRespawnClicked);
            if (root != null) root.SetActive(false);
        }

        private void OnDestroy() {
            if (respawnButton != null) respawnButton.onClick.RemoveListener(OnRespawnClicked);
            if (Instance == this) Instance = null;
        }

        /// <summary>Affiche l'écran de mort avec le délai (s) avant respawn automatique.</summary>
        public void Show(float autoRespawnSeconds) {
            _deadline = Time.time + Mathf.Max(1f, autoRespawnSeconds);
            _requested = false;
            _lastShownSeconds = -1;
            if (respawnButton != null) respawnButton.interactable = true;
            if (root != null) root.SetActive(true);
            UpdateCountdown();
        }

        public void Hide() {
            _deadline = -1f;
            if (root != null) root.SetActive(false);
        }

        private void Update() {
            if (IsOpen) UpdateCountdown();
        }

        private void UpdateCountdown() {
            if (countdownText == null || _deadline < 0f) return;
            int remaining = Mathf.Max(0, Mathf.CeilToInt(_deadline - Time.time));
            if (remaining == _lastShownSeconds) return;
            _lastShownSeconds = remaining;
            countdownText.text = _requested
                ? "Réapparition..."
                : $"Réapparition automatique dans {remaining / 60}:{remaining % 60:00}";
        }

        private void OnRespawnClicked() {
            if (_requested || PlayerController.Local == null) return;
            _requested = true;
            _lastShownSeconds = -1;
            if (respawnButton != null) respawnButton.interactable = false;
            UpdateCountdown();
            PlayerController.Local.RequestRespawn();
        }
    }
}
