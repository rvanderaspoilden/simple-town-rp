using Mirror;

/// <summary>
/// Client → Server. Sent by the Settings phone app right after a successful
/// PUT /user-settings, so the server's per-player cache stays in sync without
/// re-fetching REST. The whole UserSettingsData JSON travels in the body.
/// </summary>
public struct UserSettingsSyncMessage : NetworkMessage {
    public string dataJson;
}
