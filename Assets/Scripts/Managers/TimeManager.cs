using System;
using Mirror;
using Sim;
using UnityEngine;

public class TimeManager : MonoBehaviour {
    private static double _timeMultiplier;

    public static long StartTimestamp = 0;

    public static TimeSpan CurrentTime = TimeSpan.Zero;

    private void Awake() {
        _timeMultiplier = DatabaseManager.GameConfiguration.TimeMultiplier;
    }

    private void FixedUpdate() {
        CurrentTime = TimeSpan.FromSeconds(StartTimestamp + (NetworkTime.time * _timeMultiplier));
    }

    public static double ConvertInGameDaysToRealSeconds(float days) {
        return ((60 * 60 * 24) * days) / _timeMultiplier;
    }

    public static double ConvertInGameHourToRealSeconds(float hours) {
        return (60 * 60 * hours) / _timeMultiplier;
    }

    public static double ConvertInGameMinuteToRealSeconds(float minutes) {
        return (60 * minutes) / _timeMultiplier;
    }

    public static double ConvertRealMinuteToInGameSeconds(float minutes) {
        return (60 * minutes) * _timeMultiplier;
    }
}