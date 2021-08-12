using System;
using Mirror;
using UnityEngine;

public class TimeManager : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    private double timeMultiplier = 1;

    public static TimeSpan CurrentTime = TimeSpan.Zero;

    private void FixedUpdate() {
        CurrentTime = TimeSpan.FromSeconds(NetworkTime.time * this.timeMultiplier);
    }

    public override void OnStartServer() {
        base.OnStartServer();
    }

    public override void OnStopServer() {
        base.OnStopServer();
        
    }
}