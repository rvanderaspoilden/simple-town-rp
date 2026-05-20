using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Lightweight buy-confirmation fiche (nom + prix + Confirmer/Annuler). Subscribes
    /// to PropBehaviourBase.OnBuyRequest, emits C2S_BuyProp on confirm, and reflects
    /// the server's S2C_BuyPropResult (success/failure reason) before closing.
    /// </summary>
    public class BuyConfirmUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private TMP_Text statusLabel;   // optional feedback line
        [SerializeField] private Button   confirmButton;
        [SerializeField] private Button   cancelButton;

        public static BuyConfirmUI Instance;

        private int _propId = -1;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.confirmButton != null) this.confirmButton.onClick.AddListener(this.Confirm);
            if (this.cancelButton  != null) this.cancelButton.onClick.AddListener(this.Hide);

            // Subscribe in Awake (NOT OnEnable): the panel hides itself below, so an
            // OnEnable subscription would never fire while hidden and the trigger
            // event would never reach us. Static delegates fire regardless of active state.
            PropBehaviourBase.OnBuyRequest        += this.OnBuyRequest;
            ClientPropManager.OnBuyResultReceived += this.OnBuyResult;

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            PropBehaviourBase.OnBuyRequest        -= this.OnBuyRequest;
            ClientPropManager.OnBuyResultReceived -= this.OnBuyResult;
            if (Instance == this) Instance = null;
        }

        private void OnBuyRequest(PropBehaviourBase prop) {
            this.Show(prop);
        }

        public void Show(PropBehaviourBase prop) {
            PropIdentity id = prop != null ? prop.GetComponent<PropIdentity>() : null;
            if (id == null || id.PropId <= 0) return;

            this._propId = id.PropId;

            string displayName = prop.GetConfiguration() != null ? prop.GetConfiguration().GetDisplayName() : "";
            if (this.titleLabel != null) this.titleLabel.text = displayName;
            if (this.priceLabel != null) this.priceLabel.text = prop.Price > 0 ? $"{prop.Price} 💰" : "Gratuit";
            if (this.statusLabel != null) this.statusLabel.text = string.Empty;

            if (this.confirmButton != null) this.confirmButton.interactable = true;
            this.gameObject.SetActive(true);
        }

        public void Confirm() {
            if (this._propId <= 0) { this.Hide(); return; }
            if (this.confirmButton != null) this.confirmButton.interactable = false;
            if (this.statusLabel != null) this.statusLabel.text = "Paiement…";
            ClientPropManager.Instance?.RequestBuy(this._propId);
        }

        private void OnBuyResult(int propId, bool success, byte reason) {
            if (propId != this._propId) return;
            if (success) {
                this.Hide();
                return;
            }
            if (this.confirmButton != null) this.confirmButton.interactable = true;
            if (this.statusLabel != null) this.statusLabel.text = ReasonText(reason);
        }

        private static string ReasonText(byte reason) {
            switch (reason) {
                case 1:  return "Ce prop n'est plus disponible.";
                case 2:  return "Fonds insuffisants.";
                case 3:  return "Tu ne peux pas porter ça maintenant.";
                default: return "Achat impossible.";
            }
        }

        public void Hide() {
            this._propId = -1;
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) this.Hide();
        }
    }
}
