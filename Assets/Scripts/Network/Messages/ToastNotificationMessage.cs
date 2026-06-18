using Mirror;

/// <summary>
/// Server → Client. Affiche un toast via NotificationManager. Générique :
/// utilisable par tout système (shop, mission, building, …) en passant
/// l'id de l'app téléphone source (voir PhoneAppIds) et le texte localisé.
/// </summary>
public struct ToastNotificationMessage : NetworkMessage {
    public string text;

    /// <summary>Stable phone-app id (see <see cref="PhoneAppIds"/>). The client
    /// resolves the icon + title from the matching <see cref="PhoneApplicationUI"/>.
    /// Ignored when <see cref="worldToast"/> is true.</summary>
    public string appId;

    /// <summary>
    /// true → affiché en toast flottant au-dessus du joueur (feedback d'action banal :
    /// mains pleines, fonds insuffisants…). false (défaut) → notification coin d'écran
    /// via NotificationManager (ex. salaire périodique, messages persistants).
    /// </summary>
    public bool worldToast;

    /// <summary>Template visuel/audio du world toast : 0 = neutre, 1 = erreur, 2 = succès
    /// (mappé sur ToastKind). Ignoré si worldToast == false.</summary>
    public byte kindByte;

    public ToastKind Kind => (ToastKind)kindByte;
}
