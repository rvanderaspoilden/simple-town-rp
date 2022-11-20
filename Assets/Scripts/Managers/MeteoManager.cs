using PolyverseSkiesAsset;
using UnityEngine;

public class MeteoManager : MonoBehaviour {
    [SerializeField]
    private PolyverseSkies _polyverseSkies;

    private readonly float HOUR_CONSTANT = 1 / 12f;

    private void FixedUpdate() {
        int hours = int.Parse(TimeManager.CurrentTime.ToString(@"hh"));

        float value = 0;

        if (hours < 12) {
            value = 1 - (hours * HOUR_CONSTANT);
        } else if (hours > 12) {
            value = (hours - 12) * HOUR_CONSTANT;
        }

        this._polyverseSkies.timeOfDay = value;
    }
}