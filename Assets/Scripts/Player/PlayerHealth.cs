using System.Collections;
using Mirror;
using Sim;
using Sim.Entities;
using Sim.UI;
using UnityEngine;
using UnityEngine.Networking;

public class PlayerHealth : NetworkBehaviour {
    [Header("Only for debug")]
    [SyncVar(hook = nameof(OnHealthChanged))]
    [SerializeField]
    private float thirst;

    [SyncVar(hook = nameof(OnHealthChanged))]
    [SerializeField]
    private float hungry;

    [SyncVar(hook = nameof(OnHealthChanged))]
    [SerializeField]
    private float tiredness;

    private double lastTime;

    private PlayerController _playerController;

    private void Awake() {
        this._playerController = GetComponent<PlayerController>();
    }

    public override void OnStartServer() {
        this.lastTime = Time.time;
    }

    [Server]
    public void Init(Health health) {
        this.thirst = health.Thirst;
        this.hungry = health.Hungry;
        this.tiredness = health.Sleep;
    }

    private void Update() {
        if (!isServer) return;

        if ((Time.time - this.lastTime) > 8f) {
            this.lastTime = Time.time;
            this.DecreaseHungry();   
        }
    }

    [Client]
    public void OnHealthChanged(float old, float newValue) {
        if (!CharacterInfoPanelUI.Instance) return;

        CharacterInfoPanelUI.Instance.UpdateHealthUI(this.Health);
    }

    public Health Health => new Health() { Hungry = this.hungry, Sleep = this.tiredness, Thirst = this.thirst };

    [Server]
    private void DecreaseHungry() {
        float preview = this.hungry - 0.07f;
        this.hungry = preview > 0 ? preview : 0;

        StartCoroutine(this.UpdateDatabase());
    }

    [Server]
    public void ApplyModification(VitalNecessityType vitalNecessityType, float value) {
        switch (vitalNecessityType) {
            case VitalNecessityType.HUNGRY:
                float hungryPreview = this.hungry + value;

                if (value > 0) {
                    this.hungry = hungryPreview < 100 ? hungryPreview : 100;
                } else {
                    this.hungry = hungryPreview > 0 ? hungryPreview : 0;
                }

                break;

            case VitalNecessityType.THIRST:
                float thirstPreview = this.thirst + value;

                if (value > 0) {
                    this.thirst = thirstPreview < 100 ? thirstPreview : 100;
                } else {
                    this.thirst = thirstPreview > 0 ? thirstPreview : 0;
                }

                break;
            
            case VitalNecessityType.TIREDNESS:
                float tirednessPreview = this.tiredness + value;

                if (value > 0) {
                    this.tiredness = tirednessPreview < 100 ? tirednessPreview : 100;
                } else {
                    this.tiredness = tirednessPreview > 0 ? tirednessPreview : 0;
                }

                break;
        }

        StartCoroutine(this.UpdateDatabase());
    }

    private IEnumerator UpdateDatabase() {
        UnityWebRequest request = ApiManager.Instance.UpdateCharacterHealthRequest(this._playerController.CharacterData.Id, Health);

        yield return request.SendWebRequest();

        if (request.responseCode != 200) {
            Debug.LogError("[PlayerHealth] Cannot save health in database");
        }
    }
}