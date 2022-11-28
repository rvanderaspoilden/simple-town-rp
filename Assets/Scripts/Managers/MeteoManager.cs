using PolyverseSkiesAsset;
using UnityEngine;

public class MeteoManager : MonoBehaviour {
    [SerializeField]
    private PolyverseSkies _polyverseSkies;

    private readonly float POLYVERSE_HOUR_CONSTANT = 1 / 12f;
    private readonly float ROTATION_HOUR_CONSTANT = 360 / 24f;

    private void FixedUpdate() {
        int hours = int.Parse(TimeManager.CurrentTime.ToString(@"hh"));

        float value = 0;

        if (hours < 12) {
            value = 1 - (hours * POLYVERSE_HOUR_CONSTANT);
        } else if (hours > 12) {
            value = (hours - 12) * POLYVERSE_HOUR_CONSTANT;
        }

        this._polyverseSkies.timeOfDay = value;
        
        this.ManageRotation(hours);
    }

    private void ManageRotation(int hours) {
        this.transform.rotation = Quaternion.Euler((hours * ROTATION_HOUR_CONSTANT) + 180f,0,0);
    }
}