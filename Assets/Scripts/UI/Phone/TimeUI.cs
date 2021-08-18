using TMPro;
using UnityEngine;

public class TimeUI : MonoBehaviour {
    private TextMeshProUGUI txt;

    private void Awake() {
        this.txt = GetComponent<TextMeshProUGUI>();
    }

    private void FixedUpdate() {
        this.DisplayTime();
    }

    private void DisplayTime() {
        this.txt.text = TimeManager.CurrentTime.ToString(@"hh\:mm");
    }
}