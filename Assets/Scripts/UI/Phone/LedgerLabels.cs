using Sim.Entities.Persistence;

/// <summary>
/// French display labels for ledger reasons. Centralized so the Bank app
/// (and any future surface that lists transactions) stays consistent.
/// Falls back to the raw reason key for unknown values — preferable to
/// throwing, since the backend may introduce new reasons before the
/// client ships an update.
/// </summary>
public static class LedgerLabels {
    public static string For(string reason) {
        switch (reason) {
            case LedgerReason.ShopPurchase:      return "Achat boutique";
            case LedgerReason.DispenserPurchase: return "Achat distributeur";
            case LedgerReason.JobReward:         return "Récompense mission";
            case LedgerReason.Salary:            return "Salaire";
            case LedgerReason.DeathPenalty:      return "Vol (évanouissement)";
            case LedgerReason.P2pSale:           return "Vente";
            case LedgerReason.P2pPurchase:       return "Achat";
            case LedgerReason.GiftSent:          return "Don envoyé";
            case LedgerReason.GiftReceived:      return "Don reçu";
            case LedgerReason.RentPaid:          return "Loyer payé";
            case LedgerReason.RentReceived:      return "Loyer reçu";
            case LedgerReason.RentEvicted:       return "Pénalité d'expulsion";
            case LedgerReason.VehicleRepair:     return "Réparation véhicule";
            // Backend-only reasons (no client-side PostLedger sends these).
            case "initial_grant":                return "Allocation initiale";
            case "opening_balance":              return "Solde d'ouverture";
            case "admin_adjustment":             return "Ajustement administratif";
            default:                             return reason;
        }
    }
}
