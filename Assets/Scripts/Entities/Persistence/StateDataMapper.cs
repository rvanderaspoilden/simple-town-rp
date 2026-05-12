using System.Collections.Generic;
using Sim.Enums;
using UnityEngine;

namespace Sim.Entities.Persistence {

    /// <summary>
    /// Bridge between the JSONB `state_data` shape stored in `props.state_data`
    /// and the byte[] payload formats used at runtime by PropPayloads (DoorState,
    /// PaintBucketState, …).
    ///
    /// Convention — every state_data carries a "kind" discriminator written by
    /// the buy/build/migration flows: "door" | "bucket" | "light" | (absent → generic).
    /// </summary>
    public static class StateDataMapper {

        /// <summary>
        /// Build the runtime byte[] payload from a DB state_data row. The header
        /// (isBuilt + presetIndex) is always present; type-specific fields are
        /// layered on top. Returns null for "generic" / missing kinds — caller
        /// falls back to the ServerPropSource default payload + header override.
        /// </summary>
        public static byte[] BuildPayload(
            Dictionary<string, object> stateData,
            PropStateHeader header
        ) {
            string kind = ReadString(stateData, "kind");

            switch (kind) {
                case "door": return new DoorState {
                    Header     = header,
                    IsOpen     = ReadBool(stateData, "isOpen"),
                    LockState  = (DoorLockState) ReadInt(stateData, "lockState", (int) DoorLockState.UNLOCKED),
                    DoorNumber = ReadInt(stateData, "doorNumber", 0),
                }.Serialize();

                case "bucket": {
                    float[] color = ReadColor(stateData);
                    return new PaintBucketState {
                        Header        = header,
                        PaintConfigId = ReadInt(stateData, "paintConfigId", -1),
                        R = color.Length > 0 ? color[0] : 1f,
                        G = color.Length > 1 ? color[1] : 1f,
                        B = color.Length > 2 ? color[2] : 1f,
                    }.Serialize();
                }

                // "light" and generic props have no extra state beyond the header.
                default: return null;
            }
        }

        // ── primitive readers (tolerant of JSON.NET's loose numeric types) ──

        private static string ReadString(Dictionary<string, object> dict, string key) {
            if (dict == null || !dict.TryGetValue(key, out object v) || v == null) return null;
            return v.ToString();
        }

        private static bool ReadBool(Dictionary<string, object> dict, string key) {
            if (dict == null || !dict.TryGetValue(key, out object v) || v == null) return false;
            if (v is bool b) return b;
            return bool.TryParse(v.ToString(), out bool parsed) && parsed;
        }

        private static int ReadInt(Dictionary<string, object> dict, string key, int fallback) {
            if (dict == null || !dict.TryGetValue(key, out object v) || v == null) return fallback;
            if (v is int i) return i;
            if (v is long l) return (int) l;
            return int.TryParse(v.ToString(), out int parsed) ? parsed : fallback;
        }

        private static float[] ReadColor(Dictionary<string, object> dict) {
            if (dict == null || !dict.TryGetValue("color", out object v) || v == null) return new float[] { 1, 1, 1, 1 };

            // Newtonsoft typically deserializes a JSON array to JArray or
            // List<object>. Handle both.
            if (v is float[] arr) return arr;
            if (v is System.Collections.IList list) {
                float[] result = new float[list.Count];
                for (int i = 0; i < list.Count; i++) {
                    object item = list[i];
                    result[i] = item switch {
                        float f  => f,
                        double d => (float) d,
                        long lng => lng,
                        int integer => integer,
                        _        => float.TryParse(item?.ToString(), out float p) ? p : 1f,
                    };
                }
                return result;
            }
            return new float[] { 1, 1, 1, 1 };
        }
    }
}
