using Mirror;
using Sim.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// The "call" sub-view of the Contacts app (authored in HUD Manager.prefab,
    /// sibling of the list and SMS conversation views). Shows the peer name and,
    /// once active, a running call duration. Drives accept/decline/hangup over
    /// Mirror. Voice is handled by CallVoiceSession; view switching by ContactsUI.
    /// </summary>
    public class CallUI : MonoBehaviour {
        private enum CallUIState { Outgoing, Incoming, Active }

        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button declineButton;
        [SerializeField] private Button hangupButton;

        [Header("Ringing sound")]
        [SerializeField] private AudioSource ringSource;
        [SerializeField] private string ringClipResource = "Sounds/Phone/sfx-phone-call";

        private CallUIState _state;
        private string _peerId;
        private string _peerName;
        private float _elapsed;
        private bool _wired;

        private ContactsUI _owner;

        private void Awake() {
            _owner = GetComponentInParent<ContactsUI>(true);
            if (ringSource != null) {
                ringSource.loop = true;
                ringSource.playOnAwake = false;
                if (ringSource.clip == null && !string.IsNullOrEmpty(ringClipResource))
                    ringSource.clip = Resources.Load<AudioClip>(ringClipResource);
            }
            Wire();
        }

        private void StartRing() {
            if (ringSource != null && ringSource.clip != null && !ringSource.isPlaying) ringSource.Play();
        }

        private void StopRing() {
            if (ringSource != null && ringSource.isPlaying) ringSource.Stop();
        }

        private void Wire() {
            if (_wired) return;
            _wired = true;
            if (acceptButton != null) acceptButton.onClick.AddListener(OnAccept);
            if (declineButton != null) declineButton.onClick.AddListener(OnDecline);
            if (hangupButton != null) hangupButton.onClick.AddListener(OnHangup);
        }

        public void ShowOutgoing(string peerId, string peerName) {
            Wire();
            _state = CallUIState.Outgoing;
            _peerId = peerId;
            _peerName = string.IsNullOrEmpty(peerName) ? "Broz" : peerName;
            if (nameText != null) nameText.text = _peerName;
            if (statusText != null) statusText.text = "Appel en cours…";
            SetButtons(accept: false, decline: false, hangup: true);
            StartRing();
        }

        public void ShowIncoming(string callerId, string callerName, uint callerNetId) {
            Wire();
            _state = CallUIState.Incoming;
            _peerId = callerId;
            _peerName = string.IsNullOrEmpty(callerName) ? "Broz" : callerName;
            if (nameText != null) nameText.text = _peerName;
            if (statusText != null) statusText.text = "Appel entrant…";
            SetButtons(accept: true, decline: true, hangup: false);
            StartRing();
        }

        public void ShowActive() {
            Wire();
            _state = CallUIState.Active;
            _elapsed = 0f;
            if (statusText != null) statusText.text = "00:00";
            SetButtons(accept: false, decline: false, hangup: true);
            StopRing();
        }

        /// <summary>Server told us the call ended/failed. Show the matching feedback;
        /// view switch back to the list is done by ContactsUI.</summary>
        public void HandleEnded(CallEndReason reason) {
            StopRing();
            string notif = BuildEndNotification(reason);
            if (!string.IsNullOrEmpty(notif)) {
                NotificationManager.Instance?.AddNotification(notif, PhoneAppIds.Contacts);
            }
        }

        private void Update() {
            if (_state != CallUIState.Active || statusText == null) return;
            _elapsed += Time.deltaTime;
            int total = Mathf.FloorToInt(_elapsed);
            statusText.text = $"{total / 60:00}:{total % 60:00}";
        }

        private void OnAccept() {
            if (string.IsNullOrEmpty(_peerId)) return;
            NetworkClient.Send(new C2S_CallAccept { callerCharacterId = _peerId });
            // Active state arrives via S2C_CallAccepted.
        }

        private void OnDecline() {
            StopRing();
            if (!string.IsNullOrEmpty(_peerId)) NetworkClient.Send(new C2S_CallDecline { callerCharacterId = _peerId });
            CallVoiceSession.Close();
            _owner?.ReturnToList();
        }

        private void OnHangup() {
            StopRing();
            NetworkClient.Send(new C2S_CallHangup());
            CallVoiceSession.Close();
            _owner?.ReturnToList();
        }

        private void SetButtons(bool accept, bool decline, bool hangup) {
            if (acceptButton != null) acceptButton.gameObject.SetActive(accept);
            if (declineButton != null) declineButton.gameObject.SetActive(decline);
            if (hangupButton != null) hangupButton.gameObject.SetActive(hangup);
        }

        private string BuildEndNotification(CallEndReason reason) {
            switch (reason) {
                case CallEndReason.Declined:    return $"{_peerName} a refusé l'appel.";
                case CallEndReason.Unavailable: return $"{_peerName} est indisponible.";
                case CallEndReason.Busy:        return "Ligne occupée.";
                case CallEndReason.Timeout:
                    return _state == CallUIState.Incoming ? $"Appel manqué de {_peerName}." : "Pas de réponse.";
                case CallEndReason.Cancelled:
                    return _state == CallUIState.Incoming ? $"Appel manqué de {_peerName}." : null;
                default:                        return null; // PeerHangup on an active call: silent
            }
        }
    }
}
