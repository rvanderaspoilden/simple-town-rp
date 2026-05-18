using Mirror;

/// <summary>
/// Server → Client. Affiche un toast via NotificationManager. Générique :
/// utilisable par tout système (shop, mission, building, …) en passant le
/// type (BANK / HOSPITAL / JOB) et le texte localisé.
/// </summary>
public struct ToastNotificationMessage : NetworkMessage {
    public string text;
    public byte typeByte;

    public NotificationType Type => (NotificationType)typeByte;
}
