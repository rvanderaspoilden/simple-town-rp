using Mirror;

// Namespace global — l'enregistrement des handlers Mirror est basé réflexion,
// mélanger les namespaces casse le matching (règle projet des messages réseau).

/// <summary>Client → serveur : retirer <see cref="amount"/> au distributeur. Le serveur
/// revalide la proximité d'un ATM, le solde et la main libre, débite le compte (ledger
/// atm_withdraw) et spawne une liasse (<see cref="CashItem"/>) dans la main du joueur.</summary>
public struct C2S_AtmWithdraw : NetworkMessage {
    public int amount;
}

/// <summary>Client → serveur : déposer <see cref="amount"/> au distributeur. Le serveur
/// revalide la proximité, assemble le montant depuis les liasses SUR le joueur (main +
/// poche : liasses supprimées, au plus une décrémentée — ServerItemManager.TakeCashAmount)
/// et crédite le compte (ledger atm_deposit).</summary>
public struct C2S_AtmDeposit : NetworkMessage {
    public int amount;
}
