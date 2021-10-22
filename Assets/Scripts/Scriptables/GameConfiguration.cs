using UnityEngine;

[CreateAssetMenu(fileName = "New Game Configuration", menuName = "Configurations/Game")]
public class GameConfiguration : ScriptableObject {
    [Tooltip("This represent the number of seconds equivalent of one second in real time")]
    [SerializeField]
    private double timeMultiplier;

    [Tooltip("Interval in real seconds for server which check health")]
    [SerializeField]
    private int healthServerCheckInterval;

    [Tooltip("Number of in game days needed before dying of hungry")]
    [SerializeField]
    private float hungryDurationInDays;

    [Tooltip("Number of in game days needed before dying of thirst")]
    [SerializeField]
    private float thirstDurationInDays;

    [Tooltip("Number of in game days needed before dying of tiredness")]
    [SerializeField]
    private float tirednessDurationInDays;

    public double TimeMultiplier => timeMultiplier;

    public int HealthServerCheckInterval => healthServerCheckInterval;

    public float HungryDurationInDays => hungryDurationInDays;

    public float ThirstDurationInDays => thirstDurationInDays;

    public float TirednessDurationInDays => tirednessDurationInDays;
}