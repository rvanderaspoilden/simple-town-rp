using Dissonance;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Applies the persisted audio-device preferences to the live systems.
    /// Currently only the voice capture (microphone) device is selectable —
    /// Unity's default audio backend exposes no output-device API, so output
    /// stays whatever the OS default is.
    /// </summary>
    public static class AudioDeviceSettings {
        /// <summary>
        /// Point Dissonance at the given capture device. Empty/null selects the
        /// system default. Safe to call when no <see cref="DissonanceComms"/> is
        /// present yet — it simply no-ops.
        /// </summary>
        public static void ApplyMicrophone(string deviceName) {
            var comms = Object.FindFirstObjectByType<DissonanceComms>();
            if (comms == null) return;

            string mic = string.IsNullOrEmpty(deviceName) ? null : deviceName;
            if (comms.MicrophoneName != mic) {
                // The setter forwards to the capture pipeline, which resets the
                // microphone/encoder so the new device takes effect immediately.
                comms.MicrophoneName = mic;
            }
        }
    }
}
