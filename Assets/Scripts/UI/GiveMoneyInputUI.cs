using Mirror;
using Sim.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Lightweight, non-blocking panel to enter the amount when giving money to
    /// another player (P2P transfer). Triggered by the GIVE_MONEY action in the
    /// radial menu (PlayerController.DoAction). Emits C2S_GiveMoney on confirm.
    /// </summary>
    public class GiveMoneyInputUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private TMP_Text       titleLabel;
        [SerializeField] private TMP_InputField amountInput;
        [SerializeField] private Button         confirmButton;
        [SerializeField] private Button         cancelButton;

        public static GiveMoneyInputUI Instance;

        private uint _targetNetId;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.confirmButton != null) this.confirmButton.onClick.AddListener(this.Confirm);
            if (this.cancelButton  != null) this.cancelButton.onClick.AddListener(this.Hide);
            if (this.amountInput   != null) {
                this.amountInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                this.amountInput.onSubmit.AddListener(_ => this.Confirm());
            }

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        public void Show(PlayerController target) {
            if (target == null) return;

            this._targetNetId = target.netId;

            // Identity is gated by relationship state — mirror CameraManager.ResolveHoverName
            // so a stranger reads "Broz inconnu" in the modal too.
            string targetId = target.CharacterData?.Id;
            RelationshipState state = ClientRelationshipManager.Instance.GetState(targetId);
            string displayName = state >= RelationshipState.Acquaintance
                ? target.CharacterData?.Identity.FullName
                : "Broz inconnu";
            if (string.IsNullOrEmpty(displayName)) displayName = "Broz inconnu";
            if (this.titleLabel != null) this.titleLabel.text = $"Donner à {displayName}";

            if (this.amountInput != null) this.amountInput.text = "0";

            this.gameObject.SetActive(true);
            if (this.amountInput != null) {
                this.amountInput.Select();
                this.amountInput.ActivateInputField();
            }
        }

        public void Confirm() {
            if (this._targetNetId != 0) {
                int amount = 0;
                if (this.amountInput != null) int.TryParse(this.amountInput.text, out amount);
                amount = Mathf.Max(0, amount);

                if (amount > 0) {
                    NetworkClient.Send(new C2S_GiveMoney {
                        targetNetId = this._targetNetId,
                        amount      = amount,
                    });
                }
            }
            this.Hide();
        }

        public void Hide() {
            this._targetNetId = 0;
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) this.Hide();
        }
    }
}
