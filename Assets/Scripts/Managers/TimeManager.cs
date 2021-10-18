using System;
using Mirror;
using Sim;
using UnityEngine;

public class TimeManager : MonoBehaviour {
    private static double TimeMultiplier;

    public static long StartTimestamp = 0;

    public static TimeSpan CurrentTime = TimeSpan.Zero;

    private void Awake() {
        TimeMultiplier = DatabaseManager.GameConfiguration.TimeMultiplier;
    }

    private void FixedUpdate() {
        CurrentTime = TimeSpan.FromSeconds(StartTimestamp + (NetworkTime.time * TimeMultiplier));
    }

    public static double ConvertInGameDaysToRealSeconds(int days) {
        return ((60 * 60 * 24) * days) / TimeMultiplier;
    }
    
    public static double ConvertInGameHourToRealSeconds(int hours) {
        return (60 * 60 * hours) / TimeMultiplier;
    }
    
    public static double ConvertInGameMinuteToRealSeconds(int minutes) {
        return (60 * minutes) / TimeMultiplier;
    }
    
    public static double ConvertRealMinuteToInGameSeconds(int minutes) {
        return (60 * minutes) * TimeMultiplier;
    }
}