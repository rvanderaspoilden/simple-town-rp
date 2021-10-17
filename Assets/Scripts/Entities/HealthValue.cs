using System;
using UnityEngine;

[Serializable]
public class HealthValue {
    [SerializeField]
    private VitalNecessityType vitalNecessityType;

    [SerializeField]
    private float value;

    public VitalNecessityType VitalNecessityType => vitalNecessityType;

    public float Value => value;
}

public enum VitalNecessityType {
    HUNGRY,
    THIRST
}
