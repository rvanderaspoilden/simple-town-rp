using Mirror;

public struct UpdateCityDataMessage : NetworkMessage {
    public City City;
    // When true the client hides the loading screen after applying the city data.
    // Set to true for players that spawn in the city (no apartment), false when
    // a TeleportMessage follows (TeleportCoroutine manages the loading screen).
    public bool ShouldHideLoading;
}