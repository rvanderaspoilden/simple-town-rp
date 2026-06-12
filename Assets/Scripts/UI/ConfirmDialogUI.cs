using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Generic confirmation dialog. Call <see cref="Request"/> with a title, message and a
    /// callback; the confirm button runs the callback, cancel / Escape dismisses without
    /// running it. Authored once in the HUD prefab (singleton). If no instance is authored,
    /// <see cref="Request"/> fails open (runs the callback) so callers never get stuck.
    /// </summary>
    public class ConfirmDialogUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private Button   confirmButton;
        [SerializeField] private Button   cancelButton;

        public static ConfirmDialogUI Instance;

        private System.Action _onConfirm;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.confirmButton != null) this.confirmButton.onClick.AddListener(this.Confirm);
            if (this.cancelButton  != null) this.cancelButton.onClick.AddListener(this.Hide);

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        /// <summary>Show the dialog. <paramref name="onConfirm"/> runs only if the player confirms.</summary>
        public static void Request(string title, string message, System.Action onConfirm) {
            if (Instance == null) { onConfirm?.Invoke(); return; } // pas de dialog autoré → fail open
            Instance.Show(title, message, onConfirm);
        }

        private void Show(string title, string message, System.Action onConfirm) {
            this._onConfirm = onConfirm;
            if (this.titleLabel   != null) this.titleLabel.text   = title;
            if (this.messageLabel != null) this.messageLabel.text = message;
            this.gameObject.SetActive(true);
        }

        private void Confirm() {
            System.Action cb = this._onConfirm;
            this.Hide();
            cb?.Invoke();
        }

        public void Hide() {
            this._onConfirm = null;
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) this.Hide();
        }
    }
}
