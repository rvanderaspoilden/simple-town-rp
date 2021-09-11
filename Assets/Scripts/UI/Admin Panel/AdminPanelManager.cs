using Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AdminPanelManager : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Button buttonPrefab;

    [SerializeField]
    private Transform teleportContentTransform;

    // Start is called before the first frame update
    void Start() {
        this.InitTeleportPlaces();
    }

    private void InitTeleportPlaces() {
        foreach (var teleportPosition in FindObjectsOfType<TeleportPosition>()) {
            Button button = Instantiate(this.buttonPrefab, this.teleportContentTransform);
            button.GetComponentInChildren<TextMeshProUGUI>().text = teleportPosition.DisplayName;
            button.onClick.AddListener((() => {
                PlayerController.Local.connectionToServer.Send(new TeleportMessage() { destination = teleportPosition.GetPosition() });
            }));
        }
    }
}