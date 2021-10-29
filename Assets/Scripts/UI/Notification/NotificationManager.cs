using System;
using Sim;
using UnityEngine;

public class NotificationManager : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private NotificationUI notificationPrefab;

    [SerializeField]
    private AudioClip sound;

    [SerializeField]
    private Transform notificationContainer;
    
    public static NotificationManager Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
        } else {
            Instance = this;
        }
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.A)) {
            this.AddNotification("Test", NotificationType.BANK);
        }
    }

    public void AddNotification(string message, NotificationType type) {
        if (this.notificationContainer.childCount > 5) {
            Destroy(this.notificationContainer.GetChild(0).gameObject);
        }
        
        NotificationUI notification = Instantiate(this.notificationPrefab, this.notificationContainer);
        notification.Setup(message, DatabaseManager.NotificationTemplateConfigs.Find(x => x.NotificationType == type));
        
        HUDManager.Instance.PlaySound(this.sound, 1f);
    }
}

public enum NotificationType {
    BANK,
    HOSPITAL
}
