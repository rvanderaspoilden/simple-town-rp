using Mirror;

// ═════════════════════════════════════════════════════════════════════════════
//  NPC Interaction — messages C2S / S2C
//  Namespace global volontairement (cohérence avec les autres messages Mirror
//  du projet — voir simple-town-rp/CLAUDE.md, section "Namespaces").
//
//  Flux : le client envoie Request dès qu'il clique sur un NPC. Le serveur
//  pose un "freeze overlay" sur le NPC (stop NavMeshAgent + face joueur) tant
//  qu'au moins un owner est actif, et répond par Response (Accepted ou refus).
//  Le client envoie End quand il ferme sa modale / radial / abandonne.
//
//  Une Request supplémentaire alors qu'on est déjà owner sert de heartbeat
//  (refresh du timeout serveur). Pas de double-incrément du compteur d'owners.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Codes de raison utilisés par <see cref="S2C_NpcInteractionResponse.Reason"/>.
/// Stocké en byte sur le wire ; cet enum sert au typage côté code C# (serveur + client).
/// </summary>
public enum NpcInteractionReason : byte {
    Ok           = 0,
    NoNpc        = 1,
    OutOfRange   = 2,
    KnockedDown  = 3,
    RoomMismatch = 4
}

/// <summary>
/// Le client demande à interagir avec un NPC. Émis dès le clic (clic gauche TALK
/// ou clic droit radial), AVANT que le joueur ne soit à portée. Sert aussi de
/// heartbeat (réémis périodiquement tant qu'une modale est ouverte).
/// </summary>
public struct C2S_NpcRequestInteraction : NetworkMessage {
    public int NpcId;
}

/// <summary>
/// Le client signale la fin de son interaction avec le NPC (fermeture de modale,
/// fermeture du radial sans choix, abandon, despawn local). Idempotent côté
/// client : on n'envoie qu'une seule fois par session active.
/// </summary>
public struct C2S_NpcEndInteraction : NetworkMessage {
    public int NpcId;
}

/// <summary>
/// Réponse serveur à une Request. <see cref="Reason"/> :
///   0 = succès (le NPC est freezé, l'interaction peut commencer),
///   1 = NPC introuvable (id inconnu / despawné),
///   2 = hors-portée serveur (buffer de 5 m),
///   3 = NPC knocked down,
///   4 = mismatch de room (joueur pas dans la même room que le NPC).
///
/// Reason != 0 est aussi utilisé pour notifier la fin forcée d'une session déjà
/// ouverte (ex : le NPC vient d'être knocked down). Les clients owners ferment
/// alors leurs modales.
/// </summary>
public struct S2C_NpcInteractionResponse : NetworkMessage {
    public int  NpcId;
    public bool Accepted;
    public byte Reason;
}
