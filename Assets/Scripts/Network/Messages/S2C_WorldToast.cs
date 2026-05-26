using Mirror;

/// <summary>
/// Server → Clients : toast world-space affiché au-dessus d'un joueur précis, **visible
/// par tous** les clients qui reçoivent le message (option opt-in de synchronisation).
///
/// Par défaut les toasts sont LOCAUX (WorldToastManager.Show, au-dessus de son propre
/// joueur, sans réseau). Ne broadcaster ce message que pour les rares cas où un toast
/// doit être vu par les autres joueurs au-dessus de l'acteur. Le serveur l'envoie aux
/// connexions voulues (ex. tous les clients d'une room) ; chaque client le relaie à
/// WorldToastManager.ShowAbove(anchorNetId, ...).
/// </summary>
public struct S2C_WorldToast : NetworkMessage {
    public uint   anchorNetId; // joueur (netId) au-dessus duquel afficher le toast
    public string title;
    public string subtitle;    // vide = toast une seule ligne
    public float  delay;
}
