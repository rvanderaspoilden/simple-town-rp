using Sim.Entities.Persistence;
using TMPro;
using UnityEngine;

/// <summary>
/// Simple data-binding component on a Bank app history row template. The Bank
/// view clones the template per ledger entry, then calls <see cref="Bind"/> to
/// fill the visuals (no internal state).
/// </summary>
public class LedgerEntryRowUI : MonoBehaviour {
    [SerializeField] private TMP_Text amountLabel;
    [SerializeField] private TMP_Text reasonLabel;

    [Tooltip("Optional sub-line: counterparty name for P2P transactions, empty for system entries.")]
    [SerializeField] private TMP_Text subtitleLabel;

    private static readonly Color CreditColor = new Color(0.32f, 0.80f, 0.32f); // green
    private static readonly Color DebitColor  = new Color(0.85f, 0.25f, 0.25f); // red

    /// <param name="entry">Raw entry from the backend.</param>
    /// <param name="counterpartyName">Resolved peer name (P2P only). Empty string
    /// when the counterparty is a system source or the peer is unknown locally.</param>
    public void Bind(LedgerEntryData entry, string counterpartyName) {
        if (amountLabel != null) {
            amountLabel.text  = MoneyFormat.FormatSigned(entry.amount);
            amountLabel.color = entry.amount >= 0 ? CreditColor : DebitColor;
        }
        if (reasonLabel != null) {
            reasonLabel.text = LedgerLabels.For(entry.reason);
        }
        if (subtitleLabel != null) {
            subtitleLabel.text = counterpartyName ?? string.Empty;
        }
    }
}
