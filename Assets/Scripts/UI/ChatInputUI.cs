using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim.UI {
    public class ChatInputUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private TMP_InputField inputField;

        [SerializeField]
        private Button sendButton;

        [SerializeField]
        private Button closeButton;

        [SerializeField]
        private int maxLength = 120;

        public static ChatInputUI Instance;

        public bool IsOpen => this.gameObject.activeSelf;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;

            if (this.inputField != null) {
                this.inputField.characterLimit = this.maxLength;
                this.inputField.onSubmit.AddListener(this.OnSubmit);
            }

            if (this.sendButton != null) this.sendButton.onClick.AddListener(this.Send);
            if (this.closeButton != null) this.closeButton.onClick.AddListener(this.Hide);

            // Le panel doit être actif dans le prefab pour que Awake s'exécute
            // au chargement (Instance + listeners). Il se cache immédiatement après.
            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        public void Show() {
            this.gameObject.SetActive(true);
            if (this.inputField != null) {
                this.inputField.text = string.Empty;
                this.inputField.ActivateInputField();
                EventSystem.current?.SetSelectedGameObject(this.inputField.gameObject);
            }
            if (PlayerController.Local != null) PlayerController.Local.SetLocalWriting(true);
        }

        public void Hide() {
            if (this.inputField != null) this.inputField.DeactivateInputField();
            this.gameObject.SetActive(false);
            if (PlayerController.Local != null) PlayerController.Local.SetLocalWriting(false);
        }

        public void Toggle() {
            if (this.IsOpen) this.Hide();
            else this.Show();
        }

        private void OnSubmit(string value) => this.Send();

        public void Send() {
            string text = this.inputField != null ? this.inputField.text : null;
            if (!string.IsNullOrWhiteSpace(text) && PlayerController.Local != null) {
                PlayerController.Local.SendChatMessage(text);
            }
            this.Hide();
        }

        private void Update() {
            if (!this.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) this.Hide();
        }
    }
}
