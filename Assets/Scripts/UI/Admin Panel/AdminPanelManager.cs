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
    
    [SerializeField]
    private Transform itemSpawnerContentTransform;

    // Start is called before the first frame update
    void Start() {
        this.InitTeleportPlaces();
        this.InitSpawnerItems();
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
    
    private void InitSpawnerItems() {
        foreach (var itemConfig in DatabaseManager.ItemConfigs) {
            Button button = Instantiate(this.buttonPrefab, this.itemSpawnerContentTransform);
            button.GetComponentInChildren<TextMeshProUGUI>().text = itemConfig.Label;
            button.onClick.AddListener((() => {
                PlayerController.Local.connectionToServer.Send(new SpawnItemMessage() { itemId = itemConfig.ID, position = PlayerController.Local.transform.position});
            }));
        }
    }
}