using Mirror;
using Sim.Entities;
using UnityEngine;

/// <summary>
/// Tiny helper used by <see cref="SettingsUI"/> to push the freshly-saved
/// preferences to the Mirror server so the per-player cache (used for the
/// notification gate, etc.) stays in sync with the backend.
/// </summary>
public static class SettingsSyncBridge {
    public static void NotifyServer(UserSettingsData data) {
        if (!NetworkClient.active || !NetworkClient.isConnected || data == null) return;
        NetworkClient.Send(new UserSettingsSyncMessage {
            dataJson = JsonUtility.ToJson(data),
        });
    }
}
