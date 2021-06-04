using System;
using UnityEngine;

public class LeaveUI : PhoneApplicationUI {
    public void OnSliderValueChanged(Single value) {
        if (value >= 1f) {
            Debug.Log("LEAVE");
            Application.Quit();
        }
    }

    public override void Back() {}
}
