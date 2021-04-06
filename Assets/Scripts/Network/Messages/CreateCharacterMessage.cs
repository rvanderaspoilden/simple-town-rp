using Mirror;

public struct CreateCharacterMessage : NetworkMessage {
    public string userId;
    public string characterId;
}
