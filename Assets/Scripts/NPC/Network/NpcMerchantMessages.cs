using Mirror;

// ═════════════════════════════════════════════════════════════════════════════
//  NPC Marchand — messages C2S / S2C
//  Namespace global volontairement (cohérence avec les autres messages Mirror
//  du projet — voir simple-town-rp/CLAUDE.md, section "Namespaces").
//
//  Le catalogue n'est PAS transmis au spawn (les snapshots restent légers) : il
//  est demandé à l'ouverture de la boutique (C2S_RequestMerchantCatalog →
//  S2C_MerchantCatalog), puis chaque achat est un aller-retour
//  (C2S_MerchantBuy → S2C_MerchantBuyResult).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Le client demande le catalogue d'un marchand (à l'ouverture de la boutique).</summary>
public struct C2S_RequestMerchantCatalog : NetworkMessage {
    public int NpcId;
}

/// <summary>Une ligne du catalogue : item vendable + son prix + libellé d'affichage.</summary>
public struct MerchantCatalogEntry {
    public int    ItemConfigId;
    public int    Price;
    public string Label;
}

/// <summary>Réponse serveur : le catalogue complet du marchand demandé.</summary>
public struct S2C_MerchantCatalog : NetworkMessage {
    public int                    NpcId;
    public string                 MerchantLabel;
    public MerchantCatalogEntry[] Entries;
}

/// <summary>Le client demande l'achat d'un item précis au marchand.</summary>
public struct C2S_MerchantBuy : NetworkMessage {
    public int NpcId;
    public int ItemConfigId;
}

/// <summary>
/// Résultat d'un achat. <see cref="ReasonCode"/> :
///   0 = succès,
///   1 = item indisponible / hors catalogue,
///   2 = fonds insuffisants,
///   3 = mains pleines (ou item de mission en main),
///   4 = marchand absent / pas un marchand / hors-room.
/// </summary>
public struct S2C_MerchantBuyResult : NetworkMessage {
    public int  NpcId;
    public int  ItemConfigId;
    public bool Success;
    public byte ReasonCode;
}
