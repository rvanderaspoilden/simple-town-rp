using Mirror;

/// <summary>
/// Server → Client. Affiche une notif bancaire au owner d'une mission complétée
/// (montant gagné). Affiché via le NotificationManager existant, type BANK.
/// </summary>
public struct MissionRewardNotificationMessage : NetworkMessage {
    public int amount;
    public string label;
}
