using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// Popup shown to player B when someone wants to make acquaintance or add them
    /// to contacts. AUTHORED in HUD Manager.prefab (pattern of BuyConfirmUI): the
    /// panel GameObject hosts this component, registers Instance + button listeners
    /// in Awake, then hides itself. Accept/Refuse send a response back to the server.
    /// Identity is never shown here — only that "un Broz" wants to connect.
    /// </summary>
    public class AcquaintanceRequestUI : MonoBehaviour {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button refuseButton;

        public static AcquaintanceRequestUI Instance;

        private uint _fromNetId;
        private AcquaintanceRequestKind _kind;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;
            if (acceptButton != null) acceptButton.onClick.AddListener(() => Respond(true));
            if (refuseButton != null) refuseButton.onClick.AddListener(() => Respond(false));
            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        public void ShowRequest(uint fromNetId, AcquaintanceRequestKind kind) {
            _fromNetId = fromNetId;
            _kind = kind;
            if (label != null) {
                label.text = kind == AcquaintanceRequestKind.Contact
                    ? "Un Broz souhaite vous ajouter à ses contacts."
                    : "Un Broz souhaite faire connaissance.";
            }
            this.gameObject.SetActive(true);
        }

        public void Hide() {
            this.gameObject.SetActive(false);
        }

        private void Respond(bool accepted) {
            if (NetworkClient.active) {
                if (_kind == AcquaintanceRequestKind.Contact) {
                    NetworkClient.Send(new C2S_ContactResponse { fromNetId = _fromNetId, accepted = accepted });
                } else {
                    NetworkClient.Send(new C2S_AcquaintanceResponse { fromNetId = _fromNetId, accepted = accepted });
                }
            }
            Hide();
        }
    }
}
