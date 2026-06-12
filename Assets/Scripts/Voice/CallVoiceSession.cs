using Dissonance;
using Dissonance.Integrations.MirrorIgnorance;
using Mirror;

namespace Sim {
    /// <summary>
    /// Client-side helper that opens a private, non-positional Dissonance voice
    /// channel to a call peer for the duration of a call. Independent from the
    /// proximity push-to-talk handled by VoiceRoomAdapter — both coexist.
    /// Each client opens its own channel toward the peer (full duplex).
    /// </summary>
    public static class CallVoiceSession {
        private static PlayerChannel? _channel;

        public static void Open(uint peerNetId) {
            Close(); // never stack channels
            if (!NetworkClient.spawned.TryGetValue(peerNetId, out NetworkIdentity id) || id == null) return;

            MirrorIgnorancePlayer mip = id.GetComponentInChildren<MirrorIgnorancePlayer>();
            DissonanceComms comms = DissonanceComms.GetSingleton();
            if (mip == null || string.IsNullOrEmpty(mip.PlayerId) || comms == null || !comms.IsNetworkInitialized) return;

            _channel = comms.PlayerChannels.Open(mip.PlayerId, positional: false, priority: ChannelPriority.High);
        }

        public static void Close() {
            if (_channel.HasValue) {
                _channel.Value.Dispose();
                _channel = null;
            }
        }
    }
}
