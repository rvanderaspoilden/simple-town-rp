using System.Collections.Generic;
using Sim;
using Sim.Entities.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phone app: bank. Shows the local player's current balance and the full
/// history of ledger entries (incoming + outgoing). The actual screen layout
/// is authored in the prefab; this script wires the data flow:
///   - balance ← <see cref="PlayerBankAccount.OnLocalMoneyChanged"/>
///   - history ← <see cref="ApiManager.OnLedgerRetrieved"/> (refreshed on every
///     balance change, since a change always means a new entry was posted).
/// </summary>
public class BankUI : PhoneApplicationUI {
    [Header("Balance")]
    [SerializeField] private TMP_Text balanceLabel;

    [Header("History")]
    [SerializeField] private GameObject rowTemplate;
    [SerializeField] private RectTransform scrollContent;

    [Tooltip("Optional placeholder shown when the player has no transactions yet.")]
    [SerializeField] private GameObject emptyStatePlaceholder;

    private readonly List<GameObject> _rows = new List<GameObject>();

    private void Awake() {
        if (rowTemplate != null) rowTemplate.SetActive(false);
    }

    private void OnEnable() {
        ApiManager.OnLedgerRetrieved += OnLedgerRefreshed;
        PlayerBankAccount.OnLocalMoneyChanged += OnMoneyChanged;
        Refresh();
    }

    private void OnDisable() {
        ApiManager.OnLedgerRetrieved -= OnLedgerRefreshed;
        PlayerBankAccount.OnLocalMoneyChanged -= OnMoneyChanged;
    }

    public override void Back() {
        PhoneControllerUI.Instance?.BackToHome();
    }

    private void OnMoneyChanged(int newMoney) {
        if (balanceLabel != null) balanceLabel.text = MoneyFormat.FormatBalance(newMoney);
        // A balance change always coincides with a new ledger entry → re-pull.
        var local = PlayerController.Local;
        if (local?.CharacterData != null && ApiManager.Instance != null) {
            ApiManager.Instance.RetrieveLedger(local.CharacterData.Id);
        }
    }

    private void Refresh() {
        PlayerController local = PlayerController.Local;
        if (local == null || local.CharacterData == null) return;

        PlayerBankAccount bank = local.GetComponent<PlayerBankAccount>();
        if (balanceLabel != null && bank != null) {
            balanceLabel.text = MoneyFormat.FormatBalance(bank.Money);
        }
        if (ApiManager.Instance != null) {
            ApiManager.Instance.RetrieveLedger(local.CharacterData.Id);
        }
    }

    private void OnLedgerRefreshed(List<LedgerEntryData> entries) {
        if (rowTemplate == null || scrollContent == null) return;

        foreach (GameObject row in _rows) if (row != null) Destroy(row);
        _rows.Clear();

        bool hasEntries = entries != null && entries.Count > 0;
        if (emptyStatePlaceholder != null) emptyStatePlaceholder.SetActive(!hasEntries);
        if (!hasEntries) return;

        // Backend already returns newest first — preserve that order.
        foreach (LedgerEntryData entry in entries) {
            if (entry == null) continue;
            CreateRow(entry);
        }

        // Force the VerticalLayoutGroup + ContentSizeFitter to recompute now so
        // ScrollRect.content has the correct size on this frame (otherwise the
        // scrollbar / scroll position can be wrong until the next layout pass).
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
    }

    private void CreateRow(LedgerEntryData entry) {
        GameObject row = Instantiate(rowTemplate, scrollContent);
        row.SetActive(true);

        LedgerEntryRowUI binder = row.GetComponent<LedgerEntryRowUI>();
        if (binder != null) binder.Bind(entry, ResolveCounterpartyName(entry));

        _rows.Add(row);
    }

    private static string ResolveCounterpartyName(LedgerEntryData entry) {
        if (entry.counterpartyType != LedgerCounterparty.Player) return string.Empty;
        if (string.IsNullOrEmpty(entry.counterpartyId)) return string.Empty;

        ClientRelationshipManager mgr = ClientRelationshipManager.Instance;
        if (mgr != null && mgr.TryGet(entry.counterpartyId, out RelationshipEntry rel)) {
            return rel.FullName ?? string.Empty;
        }
        return string.Empty;
    }
}
