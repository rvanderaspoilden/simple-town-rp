using Mirror;

// Messages de la cabine d'essayage (boutique de vêtements). Global namespace — règle projet :
// les NetworkMessages restent hors namespace pour l'enregistrement Mirror.

/// <summary>Achat du panier d'essayage : ids des ClothingConfig choisis (un par catégorie max
/// côté client ; le serveur revalide catalogue/genre/fonds/place et résout le portant le plus
/// proche pour le catalogue facturé).</summary>
public struct C2S_ClothingShopPurchase : NetworkMessage {
    public int[] ConfigIds;
}

/// <summary>Résultat d'achat du panier. ReasonCode : 0 ok, 1 article indisponible, 2 fonds
/// insuffisants, 3 pas de place dans l'inventaire, 4 trop loin de la boutique.</summary>
public struct S2C_ClothingShopPurchaseResult : NetworkMessage {
    public bool Success;
    public byte ReasonCode;
    public int  TotalPaid;
}
