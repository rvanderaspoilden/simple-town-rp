using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Dialog de confirmation pour la destruction définitive d'un prop (geste irréversible).
    /// S'abonne à PropBehaviourBase.OnDestroyRequest, affiche nom + avertissement, et émet
    /// C2S_DestroyProp sur confirmation. L'autorité (propriété) est revérifiée serveur-side.
    /// </summary>
    public class DestroyConfirmUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text warningLabel;   // ligne d'avertissement optionnelle
        [SerializeField] private Button   confirmButton;
        [SerializeField] private Button   cancelButton;

        public static DestroyConfirmUI Instance;

        private string _roomId;
        private int    _propId = -1;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.confirmButton != null) this.confirmButton.onClick.AddListener(this.Confirm);
            if (this.cancelButton  != null) this.cancelButton.onClick.AddListener(this.Hide);

            // Abonnement en Awake (PAS OnEnable) : le panneau se cache ci-dessous, donc un
            // abonnement OnEnable ne se déclencherait jamais. Les délégués statiques fonctionnent
            // que l'objet soit actif ou non.
            PropBehaviourBase.OnDestroyRequest += this.OnDestroyRequest;

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            PropBehaviourBase.OnDestroyRequest -= this.OnDestroyRequest;
            if (Instance == this) Instance = null;
        }

        private void OnDestroyRequest(PropBehaviourBase prop) {
            this.Show(prop);
        }

        public void Show(PropBehaviourBase prop) {
            PropIdentity id = prop != null ? prop.GetComponent<PropIdentity>() : null;
            if (id == null || id.PropId <= 0) return;

            this._roomId = id.RoomId;
            this._propId = id.PropId;

            string displayName = prop.GetConfiguration() != null ? prop.GetConfiguration().GetDisplayName() : "";
            if (this.titleLabel != null) this.titleLabel.text = displayName;
            if (this.warningLabel != null)
                this.warningLabel.text = "Détruire cet objet définitivement ? Cette action est irréversible.";

            if (this.confirmButton != null) this.confirmButton.interactable = true;
            this.gameObject.SetActive(true);
        }

        public void Confirm() {
            if (this._propId <= 0) { this.Hide(); return; }
            NetworkClient.Send(new C2S_DestroyProp { RoomId = this._roomId, PropId = this._propId });
            this.Hide();
        }

        public void Hide() {
            this._roomId = null;
            this._propId = -1;
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) this.Hide();
        }
    }
}
