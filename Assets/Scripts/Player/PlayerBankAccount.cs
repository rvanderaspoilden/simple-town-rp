using System;
using System.Collections;
using Mirror;
using Sim;
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

    [Server]
    public void TakeMoney(int amount) {
        CharacterUpdateMoneyRequest request = new CharacterUpdateMoneyRequest() {
            money = (this.money - amount > 0) ? (this.money - amount) : 0
        };

        StartCoroutine(this.SaveCharacterMoney(request));
    }
    
    [Server]
    public void GiveMoney(int amount) {
        CharacterUpdateMoneyRequest request = new CharacterUpdateMoneyRequest() {
            money = this.money + amount
        };

        StartCoroutine(this.SaveCharacterMoney(request));
    }

    [ClientCallback]
    public void OnMoneyUpdated(int old, int newAmount) {
        if (!CharacterInfoPanelUI.Instance) return;

        CharacterInfoPanelUI.Instance.UpdateMoney(this.money);
    }
    
    private IEnumerator SaveCharacterMoney(CharacterUpdateMoneyRequest moneyRequest) {
        UnityWebRequest request = ApiManager.Instance.UpdateCharacterMoneyRequest(this._playerController.CharacterData.Id, moneyRequest);

        yield return request.SendWebRequest();

        if (request.responseCode == 200) {
            this.money = moneyRequest.money;
        } else {
            Debug.LogError($"[PlayerBankAccount] Cannot save bank account in database for [name={this.name}]");
        }
    }
}
