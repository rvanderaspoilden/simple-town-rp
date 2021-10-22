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

    private double _lastTime;
    
    private readonly float VITAL_NECESSITY_MIN_VALUE = 0;
    private readonly float VITAL_NECESSITY_MAX_VALUE = 100;

    private PlayerController _playerController;

    private void Awake() {
        this._playerController = GetComponent<PlayerController>();
    }

    public override void OnStartServer() {
        this._lastTime = Time.time;
    }

    [Server]
    public void Init(Health health) {
        this.thirst = health.Thirst;
        this.hungry = health.Hungry;
        this.tiredness = health.Sleep;
    }

    private void Update() {
        if (!isServer) return;

        if ((Time.time - this._lastTime) > DatabaseManager.GameConfiguration.HealthServerCheckInterval) {
            this._lastTime = Time.time;
            this.DecreaseVitalNecessities();
        }
    }

    [Client]
    public void OnHealthChanged(float old, float newValue) {
        if (!CharacterInfoPanelUI.Instance) return;

        CharacterInfoPanelUI.Instance.UpdateHealthUI(this.Health);
    }

    public Health Health => new Health() { Hungry = this.hungry, Sleep = this.tiredness, Thirst = this.thirst };

    [Server]
    private void DecreaseVitalNecessities() {
        this.hungry = this.GetDecreasedValue(this.hungry, DatabaseManager.GameConfiguration.HungryDurationInDays);
        this.thirst = this.GetDecreasedValue(this.thirst, DatabaseManager.GameConfiguration.ThirstDurationInDays);
        this.tiredness = this.GetDecreasedValue(this.tiredness, DatabaseManager.GameConfiguration.TirednessDurationInDays);

        StartCoroutine(this.UpdateDatabase());
    }

    private float GetDecreasedValue(float initialValue, float referenceDuration) {
        double valueToDecrease = (DatabaseManager.GameConfiguration.HealthServerCheckInterval * 100) / TimeManager.ConvertInGameDaysToRealSeconds(referenceDuration);
        float preview = initialValue - (float) valueToDecrease;
        return preview > 0 ? preview : 0;
    }

    private float GetAppliedValue(float initialValue, float valueToAdd, float min, float max) {
        float preview = initialValue + valueToAdd;

        if (valueToAdd > 0) { // Case of additive value
            return preview < max ? preview : max;
        }

        return preview > min ? preview : min;
    }

    [Server]
    public void ApplyModification(VitalNecessityType vitalNecessityType, float value) {
        switch (vitalNecessityType) {
            case VitalNecessityType.HUNGRY:
                this.hungry = this.GetAppliedValue(this.hungry, value, VITAL_NECESSITY_MIN_VALUE, VITAL_NECESSITY_MAX_VALUE);
                break;

            case VitalNecessityType.THIRST:
                this.thirst = this.GetAppliedValue(this.thirst, value, VITAL_NECESSITY_MIN_VALUE, VITAL_NECESSITY_MAX_VALUE);
                break;

            case VitalNecessityType.TIREDNESS:
                this.tiredness = this.GetAppliedValue(this.tiredness, value, VITAL_NECESSITY_MIN_VALUE, VITAL_NECESSITY_MAX_VALUE);
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