/// <summary>
/// Currency formatter used by every money-displaying surface (Bank app,
/// future wallet HUD…). One source of truth for the unit symbol — the
/// codebase had `€` and `BC` mixed; `€` wins (already used by CareerUI
/// and the salary toasts).
/// </summary>
public static class MoneyFormat {
    private const string Unit = "€";

    /// <summary>Signed amount for ledger lines — "+50 €" / "-12 €".
    /// Zero is rendered without sign.</summary>
    public static string FormatSigned(int amount) {
        if (amount > 0) return $"+{amount} {Unit}";
        if (amount < 0) return $"-{-amount} {Unit}";
        return $"0 {Unit}";
    }

    /// <summary>Unsigned balance — "312 €". Negative balances (overdraft)
    /// keep the minus sign.</summary>
    public static string FormatBalance(int amount) => $"{amount} {Unit}";
}
