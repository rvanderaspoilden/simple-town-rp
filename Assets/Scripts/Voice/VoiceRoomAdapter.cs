using Dissonance;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Adapts Dissonance voice channels to the current game room.
    /// Placed on the "Dissonance" child of the player prefab.
    /// Only active for the local player — initialized by PlayerController.OnStartLocalPlayer.
    ///
    /// Both broadcast and receipt triggers are pointed at the same channel name as
    /// the current game room (e.g. "city", "hall:Main:1"). The existing SphereCollider
    /// on this GameObject handles spatial proximity — voice is only exchanged when
    /// two players are physically within range AND in the same room.
    /// </summary>
    public class VoiceRoomAdapter : MonoBehaviour {
        private VoiceBroadcastTrigger _broadcast;
        private VoiceReceiptTrigger   _receipt;

        [Tooltip("Push-to-talk key — voice is only transmitted while this key is held.")]
        [SerializeField] private KeyCode pushToTalkKey = KeyCode.V;

        private bool _isLocal;

        private void Awake() {
            _broadcast = GetComponent<VoiceBroadcastTrigger>();
            _receipt   = GetComponent<VoiceReceiptTrigger>();
            // Inert until proven local. The triggers transmit/receive the LOCAL
            // microphone, so a copy on every remote player instance would make the
            // local mic broadcast through each one. Only the local player's adapter
            // re-enables them in OnLocalPlayerStart.
            if (_broadcast != null) { _broadcast.Mode = CommActivationMode.None; _broadcast.enabled = false; }
            if (_receipt   != null) _receipt.enabled = false;
        }

        public void OnLocalPlayerStart() {
            if (_broadcast == null) _broadcast = GetComponent<VoiceBroadcastTrigger>();
            if (_receipt   == null) _receipt   = GetComponent<VoiceReceiptTrigger>();
            // Push-to-talk: start muted; Update() opens the channel only while the key is held.
            if (_broadcast != null) { _broadcast.enabled = true; _broadcast.Mode = CommActivationMode.None; }
            if (_receipt   != null) _receipt.enabled = true;
            _isLocal = true;
            ClientPropManager.OnLocalRoomChanged += SwitchRoom;
            SwitchRoom("city");
            ApplySavedMicrophone();
        }

        private void Update() {
            if (!_isLocal || _broadcast == null) return;
            _broadcast.Mode = Input.GetKey(this.pushToTalkKey)
                ? CommActivationMode.Open
                : CommActivationMode.None;
        }

        private static void ApplySavedMicrophone() {
            var settings = ApiManager.Instance != null ? ApiManager.Instance.UserSettings : null;
            if (settings != null) AudioDeviceSettings.ApplyMicrophone(settings.Data.MicrophoneDevice);
        }

        public void OnLocalPlayerStop() {
            ClientPropManager.OnLocalRoomChanged -= SwitchRoom;
        }

        private void SwitchRoom(string roomId) {
            if (_broadcast != null) _broadcast.RoomName = roomId;
            if (_receipt   != null) _receipt.RoomName   = roomId;
        }
    }
}
