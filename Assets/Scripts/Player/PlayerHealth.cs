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

        this.CheckDeath();
    }

    private void Update() {
        if (!isServer) return;

        if (this._playerController.Died) {
            this._lastTime = Time.time;
            return;
        }

        if ((Time.time - this._lastTime) > DatabaseManager.GameConfiguration.HealthServerCheckInterval) {
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
        Health health = Health;
        
        if (this.hungry > 0) {
            health.Hungry = this.GetDecreasedValue(this.hungry, DatabaseManager.GameConfiguration.HungryDurationInDays);
        }

        if (this.thirst > 0) {
            health.Thirst = this.GetDecreasedValue(this.thirst, DatabaseManager.GameConfiguration.ThirstDurationInDays);
        }

        if (this.tiredness > 0) {
            health.Sleep = this.GetDecreasedValue(this.tiredness, DatabaseManager.GameConfiguration.TirednessDurationInDays);
        }

        this.CheckDeath();

        StartCoroutine(this.SaveHealth(health));
        
        this._lastTime = Time.time;
    }

    private float GetDecreasedValue(float initialValue, float referenceDuration) {
        double valueToDecrease = ((Time.time - this._lastTime) * 100) / TimeManager.ConvertInGameDaysToRealSeconds(referenceDuration);
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

    private void CheckDeath() {
        if (this.hungry == 0 || this.thirst == 0 || this.tiredness == 0) {
            this._playerController.Kill();
        }
    }

    [Server]
    public void ApplyModifications(HealthValue[] impacts) {
        Health health = Health;
        
        foreach (var healthValue in impacts) {
            switch (healthValue.VitalNecessityType) {
                case VitalNecessityType.HUNGRY:
                    health.Hungry = this.GetDecreasedValue(this.hungry, DatabaseManager.GameConfiguration.HungryDurationInDays);
                    health.Hungry = this.GetAppliedValue(health.Hungry, healthValue.Value, VITAL_NECESSITY_MIN_VALUE, VITAL_NECESSITY_MAX_VALUE);
                    break;

                case VitalNecessityType.THIRST:
                    health.Thirst = this.GetDecreasedValue(this.thirst, DatabaseManager.GameConfiguration.ThirstDurationInDays);
                    health.Thirst = this.GetAppliedValue(this.thirst, healthValue.Value, VITAL_NECESSITY_MIN_VALUE, VITAL_NECESSITY_MAX_VALUE);
                    break;

                case VitalNecessityType.TIREDNESS:
                    health.Sleep = this.GetDecreasedValue(this.tiredness, DatabaseManager.GameConfiguration.TirednessDurationInDays);
                    health.Sleep = this.GetAppliedValue(this.tiredness, healthValue.Value, VITAL_NECESSITY_MIN_VALUE, VITAL_NECESSITY_MAX_VALUE);
                    break;
            }   
        }

        StartCoroutine(this.SaveHealth(health));

        this._lastTime = Time.time;
    }

    [Server]
    public void ResetAll() {
        Health health = new Health() {
            Thirst = VITAL_NECESSITY_MAX_VALUE,
            Hungry = VITAL_NECESSITY_MAX_VALUE,
            Sleep = VITAL_NECESSITY_MAX_VALUE,
        };
        
        StartCoroutine(this.SaveHealth(health));
    }

    private IEnumerator SaveHealth(Health health) {
        UnityWebRequest request = ApiManager.Instance.UpdateCharacterHealthRequest(this._playerController.CharacterData.Id, health);

        yield return request.SendWebRequest();

        if (request.responseCode == 200) {
            this.thirst = health.Thirst;
            this.hungry = health.Hungry;
            this.tiredness = health.Sleep;
        } else {
            Debug.LogError("[PlayerHealth] Cannot save health in database");
        }
    }
}