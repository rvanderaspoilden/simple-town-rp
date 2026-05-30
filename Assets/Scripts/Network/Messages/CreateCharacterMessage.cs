using Mirror;

public struct CreateCharacterMessage : NetworkMessage {
    public string userId;
    public string characterId;
    /// <summary>
    /// Stress-test only. When true, the server skips the apartment instantiation
    /// (BuildingBehavior.TeleportToApartment + HallController + room build) and
    /// spawns the player directly in the city. Each instantiated apartment costs
    /// ~50-100 MB server-side; bots don't need their home so we save that memory.
    /// </summary>
    public bool spawnInCity;
}
