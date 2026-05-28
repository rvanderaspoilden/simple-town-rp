using System;
using System.Collections;
using Mirror;
using Newtonsoft.Json;
using Sim;
using Sim.Entities.Persistence;
using Sim.UI;
using UnityEngine;
using UnityEngine.Networking;

public class PlayerBankAccount : NetworkBehaviour {
    [SyncVar(hook = nameof(OnMoneyUpdated))]
    [SerializeField]
    private int money;

    private PlayerController _playerController;

    private void Awake() {
        this._playerController = GetComponent<PlayerController>();
    }

    public int Money => money;

    [Server]
    public void Init(int amount) {
        this.money = amount;
    }

    /// <summary>
    /// Single chokepoint for EVERY money movement on this account (shop, dispenser,
    /// job reward, salary, death penalty, p2p sale/gift). Posts a signed amount
    /// (+credit / -debit) + reason to the ledger; the backend updates the balance
    /// AND records the entry atomically, then we sync the authoritative balance it
    /// returns. Funds must be validated by the caller before debiting.
    /// </summary>
    [Server]
    public void PostLedger(int signedAmount, string reason, string counterpartyType, string counterpartyId,
                           string propId = null, int? configId = null) {
        PostLedgerBody body = new PostLedgerBody {
            amount = signedAmount,
            reason = reason,
            counterpartyType = counterpartyType,
            counterpartyId = counterpartyId,
            propId = propId,
            configId = configId,
        };

        StartCoroutine(this.PostLedgerCoroutine(body));
    }

    [ClientCallback]
    public void OnMoneyUpdated(int old, int newAmount) {
        if (!CharacterInfoPanelUI.Instance) return;

        CharacterInfoPanelUI.Instance.UpdateMoney(this.money);
    }

    private IEnumerator PostLedgerCoroutine(PostLedgerBody body) {
        UnityWebRequest request = ApiManager.Instance.PostLedgerEntryRequest(this._playerController.CharacterData.Id, body);

        yield return request.SendWebRequest();

        if (request.responseCode == 200 || request.responseCode == 201) {
            try {
                LedgerPostResponse response = JsonConvert.DeserializeObject<LedgerPostResponse>(request.downloadHandler.text);
                if (response != null) this.money = response.money;
            } catch (Exception e) {
                Debug.LogError($"[PlayerBankAccount] Cannot parse ledger response for [name={this.name}]: {e.Message}");
            }

            // Notification banque uniquement sur les DÉPENSES (amount < 0). On affiche
            // strictement le montant de la dépense (positif, signé '-') côté joueur.
            if (body.amount < 0 && connectionToClient != null) {
                connectionToClient.Send(new ToastNotificationMessage {
                    text       = $"-{-body.amount} BC",
                    typeByte   = (byte)NotificationType.BANK,
                    worldToast = false,
                });
            }
        } else {
            Debug.LogError($"[PlayerBankAccount] Ledger post failed for [name={this.name}] reason={body.reason} code={request.responseCode}");
        }
    }
}
