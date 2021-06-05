using System;
using UnityEngine;

public class LeaveUI : PhoneApplicationUI {
    public void OnSliderValueChanged(Single value) {
        if (value >= 1f) {
            Application.Quit();
        }
    }

    public override void Back() { }
}