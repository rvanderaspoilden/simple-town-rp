using Mirror;

/// <summary>
/// Server → Client. Notification UI relative au métier (mission disponible,
/// prise, abandonnée…). Le serveur construit le texte final ; le client
/// l'affiche tel quel via NotificationManager (type JOB).
/// </summary>
public struct JobNotificationMessage : NetworkMessage {
    public string text;
}
