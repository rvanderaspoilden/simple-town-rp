using System;
using Mirror;
using UnityEngine;

public class TimeManager : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private double timeMultiplier = 1;

    public static long StartTimestamp = 0;

    public static TimeSpan CurrentTime = TimeSpan.Zero;
    
    private void FixedUpdate() {
        CurrentTime = TimeSpan.FromSeconds(StartTimestamp + (NetworkTime.time * this.timeMultiplier));
    }
}