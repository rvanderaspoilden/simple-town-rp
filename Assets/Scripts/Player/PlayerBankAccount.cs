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

    /// <summary>Client-side push for any UI that needs to react to the local
    /// player's balance changing (Bank app, future wallet, etc.). Fires only
    /// on the local player's account, on every SyncVar update including the
    /// initial sync after spawn.</summary>
    public static event Action<int> OnLocalMoneyChanged;

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
    /// Sets the SyncVar to an authoritative balance computed server-side WITHOUT
    /// posting a ledger entry. Use when the balance changed in the DB through a
    /// path that bypasses PostLedger (e.g. the rent collect_rent RPC), to keep
    /// online players' displayed money in sync.
    /// </summary>
    [Server]
    public void SetAuthoritativeBalance(int amount) {
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

    private bool _seenFirstMoney;

    [ClientCallback]
    public void OnMoneyUpdated(int old, int newAmount) {
        // Son d'encaissement pour le joueur LOCAL uniquement, sur une vraie hausse (pas la
        // synchro initiale au spawn). Les dépenses ont déjà leur notification banque.
        if (isLocalPlayer) {
            if (_seenFirstMoney && newAmount > old)
                Sim.Audio.AudioManager.Instance.PlayUI(Sim.Audio.SfxId.MoneyReceive);
            _seenFirstMoney = true;

            OnLocalMoneyChanged?.Invoke(newAmount);
        }

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
                    appId      = PhoneAppIds.Bank,
                    worldToast = false,
                });
            }
        } else {
            Debug.LogError($"[PlayerBankAccount] Ledger post failed for [name={this.name}] reason={body.reason} code={request.responseCode}");
        }
    }
}
