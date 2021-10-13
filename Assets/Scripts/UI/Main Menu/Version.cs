using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public class Version : MonoBehaviour {
    private void Start() {
        GetComponent<TextMeshProUGUI>().text = $"(version {Application.version})";
    }
}
