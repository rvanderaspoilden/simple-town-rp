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

        public void OnLocalPlayerStart() {
            _broadcast = GetComponent<VoiceBroadcastTrigger>();
            _receipt   = GetComponent<VoiceReceiptTrigger>();
            ClientPropManager.OnLocalRoomChanged += SwitchRoom;
            SwitchRoom("city");
            ApplySavedMicrophone();
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
