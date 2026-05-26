using Mirror;

/// <summary>
/// Server → Client. Affiche un toast via NotificationManager. Générique :
/// utilisable par tout système (shop, mission, building, …) en passant le
/// type (BANK / HOSPITAL / JOB) et le texte localisé.
/// </summary>
public struct ToastNotificationMessage : NetworkMessage {
    public string text;
    public byte typeByte;

    /// <summary>
    /// true → affiché en toast flottant au-dessus du joueur (feedback d'action banal :
    /// mains pleines, fonds insuffisants…). false (défaut) → notification coin d'écran
    /// via NotificationManager (ex. salaire périodique, messages persistants).
    /// </summary>
    public bool worldToast;

    public NotificationType Type => (NotificationType)typeByte;
}
