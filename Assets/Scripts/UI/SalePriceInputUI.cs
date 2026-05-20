using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Lightweight, non-blocking panel to set a price when the owner lists a prop
    /// for sale. Subscribes to PropBehaviourBase.OnListForSaleRequest and emits
    /// C2S_SetPropForSale via ClientPropManager on confirm.
    /// </summary>
    public class SalePriceInputUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private TMP_Text       titleLabel;
        [SerializeField] private TMP_InputField priceInput;
        [SerializeField] private Button         confirmButton;
        [SerializeField] private Button         cancelButton;

        public static SalePriceInputUI Instance;

        private int _propId = -1;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.confirmButton != null) this.confirmButton.onClick.AddListener(this.Confirm);
            if (this.cancelButton  != null) this.cancelButton.onClick.AddListener(this.Hide);
            if (this.priceInput    != null) {
                this.priceInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                this.priceInput.onSubmit.AddListener(_ => this.Confirm());
            }

            // Subscribe in Awake (NOT OnEnable): the panel hides itself below, so
            // OnEnable wouldn't fire while hidden and the trigger event would never
            // be received. The static delegate keeps calling us even while inactive.
            PropBehaviourBase.OnListForSaleRequest += this.OnListForSaleRequest;

            // Must be active in the scene so this Awake runs at load; hide right after.
            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            PropBehaviourBase.OnListForSaleRequest -= this.OnListForSaleRequest;
            if (Instance == this) Instance = null;
        }

        private void OnListForSaleRequest(PropBehaviourBase prop) {
            this.Show(prop);
        }

        public void Show(PropBehaviourBase prop) {
            PropIdentity id = prop != null ? prop.GetComponent<PropIdentity>() : null;
            if (id == null || id.PropId <= 0) return;

            this._propId = id.PropId;

            string displayName = prop.GetConfiguration() != null ? prop.GetConfiguration().GetDisplayName() : "";
            if (this.titleLabel != null) this.titleLabel.text = $"Mettre en vente — {displayName}";

            if (this.priceInput != null) {
                int suggested = prop.GetConfiguration() != null ? prop.GetConfiguration().Price : 0;
                this.priceInput.text = Mathf.Max(0, suggested).ToString();
            }

            this.gameObject.SetActive(true);
            if (this.priceInput != null) {
                this.priceInput.Select();
                this.priceInput.ActivateInputField();
            }
        }

        public void Confirm() {
            if (this._propId > 0) {
                int price = 0;
                if (this.priceInput != null) int.TryParse(this.priceInput.text, out price);
                ClientPropManager.Instance?.RequestSetForSale(this._propId, Mathf.Max(0, price));
            }
            this.Hide();
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
