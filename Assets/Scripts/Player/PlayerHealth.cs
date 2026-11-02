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

    // Hit points. Death is driven by this alone: starvation/dehydration (hunger or
    // thirst at 0) drains it over time, weapon hits subtract from it, and it slowly
    // regenerates while the player is well-fed. The "energy" HUD bar is now a local
    // stamina gauge driven by PlayerController (running/jumping), unrelated to this.
    [SyncVar(hook = nameof(OnHealthChanged))]
    [SerializeField]
    private float pv;

    private double _lastTime;

    private readonly float VITAL_NECESSITY_MIN_VALUE = 0;
    private readonly float VITAL_NECESSITY_MAX_VALUE = 100;

    // In-game days to regenerate full PV while both hunger and thirst are above the
    // "fed" threshold — slow so weapon damage is survivable without a healing item,
    // without trivialising starvation.
    private const float HealthRegenDurationInDays = 1f;

    // Above this (0-100) a vital counts as "fed" and stops draining / allows regen.
    private const float FedThreshold = 15f;

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
        this.pv = health.Pv;

        this.CheckDeath();
    }

    private void Update() {
        if (!isServer) return;

        if (this._playerController.PlayerState == PlayerState.DIED) {
            this._lastTime = Time.time;
            return;
        }

        if (this._playerController.PlayerState == PlayerState.SLEEPING) {
            this.ManageVitalNecessitiesWhenSleeping();
            return;
        }

        if ((Time.time - this._lastTime) > DatabaseManager.GameConfiguration.HealthServerCheckInterval) {
            this.DecreaseVitalNecessities();
        }
    }

    [Client]
    public void OnHealthChanged(float old, float newValue) {
        if (!isLocalPlayer || !PlayerStatsUI.Instance) return;

        PlayerStatsUI.Instance.UpdateStats(this.Health);
    }

    public Health Health => new Health() { Hungry = this.hungry, Sleep = this.tiredness, Thirst = this.thirst, Pv = this.pv };

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

        health.Pv = this.ComputePvAfterTick(health.Hungry, health.Thirst, this.pv);

        this.CheckDeath();

        StartCoroutine(this.SaveHealth(health));

        this._lastTime = Time.time;
    }

    [Server]
    private void ManageVitalNecessitiesWhenSleeping() {
        if (this.hungry > 0) {
            this.hungry = this.GetDecreasedValue(this.hungry, DatabaseManager.GameConfiguration.HungryDurationInDays * 2);
        }

        if (this.thirst > 0) {
            this.thirst = this.GetDecreasedValue(this.thirst, DatabaseManager.GameConfiguration.ThirstDurationInDays * 2);
        }

        if (this.tiredness < 100) {
            this.tiredness = this.GetIncreasedValue(this.tiredness, DatabaseManager.GameConfiguration.TirednessRecoveryDuration);
        }

        // Starving/dehydrated while asleep still drains PV (energy recovers, the belly
        // doesn't). Applied directly to the SyncVar like the vitals on this path.
        this.pv = this.ComputePvAfterTick(this.hungry, this.thirst, this.pv);
        this.CheckDeath();

        this._lastTime = Time.time;
    }

    /// <summary>PV delta for one server tick: drains once per zeroed vital (hunger at 0
    /// and thirst at 0 stack to 2x), slowly regenerates when both are above the fed
    /// threshold, otherwise holds steady.</summary>
    [Server]
    private float ComputePvAfterTick(float hungryValue, float thirstValue, float currentPv) {
        float result = currentPv;

        bool starving = hungryValue <= VITAL_NECESSITY_MIN_VALUE;
        bool dehydrated = thirstValue <= VITAL_NECESSITY_MIN_VALUE;

        if (starving) {
            result = this.GetDecreasedValue(result, DatabaseManager.GameConfiguration.HealthDrainDurationInDays);
        }
        if (dehydrated) {
            result = this.GetDecreasedValue(result, DatabaseManager.GameConfiguration.HealthDrainDurationInDays);
        }

        if (!starving && !dehydrated && hungryValue >= FedThreshold && thirstValue >= FedThreshold && result < VITAL_NECESSITY_MAX_VALUE) {
            result = this.GetIncreasedValueByDays(result, HealthRegenDurationInDays);
        }

        return result;
    }

    /// <summary>Applies weapon damage to the player's PV (server-authoritative). Ignored
    /// when already dead. Persists the new value and triggers death at 0 (ragdoll + corps
    /// fouillable, cf. PlayerController.Kill). Coup NON létal → petit choc procédural
    /// répliqué (RpcFlinch). Renvoie vrai si CE coup a tué le joueur — l'attaquant colore
    /// son feedback « coup fatal ».</summary>
    [Server]
    public bool ApplyWeaponDamage(float amount) {
        if (amount <= 0) return false;
        if (this._playerController.PlayerState == PlayerState.DIED) return false;

        // Combat is authoritative in-memory and must not wait on the REST round-trip:
        // apply the PV immediately (so death is instant for a lethal hit, and the HUD
        // SyncVar updates at once), then persist in the background.
        this.pv = this.GetAppliedValue(this.pv, -amount, VITAL_NECESSITY_MIN_VALUE, VITAL_NECESSITY_MAX_VALUE);

        bool died = this.pv <= VITAL_NECESSITY_MIN_VALUE;
        this.CheckDeath();
        if (!died) this.RpcFlinch();

        StartCoroutine(this.PersistHealth(Health));
        return died;
    }

    /// <summary>Choc procédural du buste sur toutes les copies (coup d'arme NON létal).</summary>
    [ClientRpc]
    private void RpcFlinch() {
        CharacterFlinch.PlayOn(gameObject);
    }

    /// <summary>Persists the current health to the backend WITHOUT writing the values back
    /// on success — the SyncVars are already authoritative (used by the combat path so a
    /// slow REST reply can't clobber a newer PV).</summary>
    private IEnumerator PersistHealth(Health health) {
        UnityWebRequest request = ApiManager.Instance.UpdateCharacterHealthRequest(this._playerController.CharacterData.Id, health);
        yield return request.SendWebRequest();
        if (request.responseCode != 200) {
            Debug.LogError("[PlayerHealth] Cannot save health in database");
        }
    }

    [Server]
    private float GetDecreasedValue(float initialValue, float referenceDuration) {
        double valueToDecrease = ((Time.time - this._lastTime) * 100) / TimeManager.ConvertInGameDaysToRealSeconds(referenceDuration);
        float preview = initialValue - (float) valueToDecrease;
        return preview > VITAL_NECESSITY_MIN_VALUE ? preview : VITAL_NECESSITY_MIN_VALUE;
    }

    [Server]
    private float GetIncreasedValue(float initialValue, float referenceDuration) {
        double valueToIncrease = ((Time.time - this._lastTime) * 100) / referenceDuration;
        float preview = initialValue + (float) valueToIncrease;
        return preview < VITAL_NECESSITY_MAX_VALUE ? preview : VITAL_NECESSITY_MAX_VALUE;
    }

    [Server]
    private float GetIncreasedValueByDays(float initialValue, float referenceDurationInDays) {
        double valueToIncrease = ((Time.time - this._lastTime) * 100) / TimeManager.ConvertInGameDaysToRealSeconds(referenceDurationInDays);
        float preview = initialValue + (float) valueToIncrease;
        return preview < VITAL_NECESSITY_MAX_VALUE ? preview : VITAL_NECESSITY_MAX_VALUE;
    }

    [Server]
    private float GetAppliedValue(float initialValue, float valueToAdd, float min, float max) {
        float preview = initialValue + valueToAdd;

        if (valueToAdd > 0) { // Case of additive value
            return preview < max ? preview : max;
        }

        return preview > min ? preview : min;
    }

    [Server]
    private void CheckDeath() {
        if (this.pv <= VITAL_NECESSITY_MIN_VALUE) {
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
            Pv = VITAL_NECESSITY_MAX_VALUE,
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
            this.pv = health.Pv;
        } else {
            Debug.LogError("[PlayerHealth] Cannot save health in database");
        }
    }
}
