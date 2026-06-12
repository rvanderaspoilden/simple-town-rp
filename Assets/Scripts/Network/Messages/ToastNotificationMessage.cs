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

    /// <summary>Template visuel/audio du world toast : 0 = neutre, 1 = erreur, 2 = succès
    /// (mappé sur ToastKind). Ignoré si worldToast == false.</summary>
    public byte kindByte;

    public NotificationType Type => (NotificationType)typeByte;
    public ToastKind Kind => (ToastKind)kindByte;
}
