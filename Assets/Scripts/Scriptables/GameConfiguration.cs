using UnityEngine;

[CreateAssetMenu(fileName = "New Game Configuration", menuName = "Configurations/Game")]
public class GameConfiguration : ScriptableObject {
    [Tooltip("This represent the number of seconds equivalent of one second in real time")]
    [SerializeField]
    private double timeMultiplier;

    [Tooltip("Number of real seconds needed before dying of hungry")]
    [SerializeField]
    private float hungryDurationInSeconds;

    [Tooltip("Number of real seconds needed before dying of thirst")]
    [SerializeField]
    private float thirstDurationInSeconds;

    [Tooltip("Number of real seconds needed before dying of tiredness")]
    [SerializeField]
    private float tirednessDurationInSeconds;

    public double TimeMultiplier => timeMultiplier;

    public float HungryDurationInSeconds => hungryDurationInSeconds;

    public float ThirstDurationInSeconds => thirstDurationInSeconds;

    public float TirednessDurationInSeconds => tirednessDurationInSeconds;
}