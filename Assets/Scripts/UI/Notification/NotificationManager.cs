using System;
using System.Linq;
using DG.Tweening;
using Sim;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

public class NotificationManager : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private NotificationUI notificationPrefab;

    [SerializeField]
    private AudioClip sound;

    [SerializeField]
    private Transform notificationContainer;

    private VerticalLayoutGroup _verticalLayoutGroup;

    private bool _isMovingNotifications;

    public static NotificationManager Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
        } else {
            Instance = this;
            this._verticalLayoutGroup = GetComponent<VerticalLayoutGroup>();
        }
    }

    public void AddNotification(string message, NotificationType type) {
        if (this.notificationContainer.childCount >= 5) {
            Destroy(this.notificationContainer.GetChild(0).gameObject);
        }

        NotificationUI notification = Instantiate(this.notificationPrefab, this.notificationContainer);
        notification.Setup(message, DatabaseManager.NotificationTemplateConfigs.Find(x => x.NotificationType == type));

        HUDManager.Instance.PlaySound(this.sound, 1f);

        if (notification.transform.GetSiblingIndex() == 0) {
            notification.SetAutoHide(5);
        }
        
        LayoutRebuilder.ForceRebuildLayoutImmediate(notification.RectTransform);
    }

    public void RemoveNotification(NotificationUI notification) {
        this._isMovingNotifications = true;

        notification.RectTransform
            .DOAnchorPosX(notification.RectTransform.anchoredPosition.x + notification.RectTransform.sizeDelta.x, .5f)
            .OnComplete(() => {
                if (this.transform.childCount > notification.transform.GetSiblingIndex() + 1) {
                    float heightOffset = notification.RectTransform.sizeDelta.y + this._verticalLayoutGroup.spacing;
                    this.MoveDownNotification(notification.transform.GetSiblingIndex(), heightOffset);
                    Destroy(notification.gameObject, .5f);
                } else {
                    Destroy(notification.gameObject);
                }
            });
    }

    public void DeleteAllNotification() {
        foreach (Transform child in this.transform) {
            Destroy(child.gameObject);
        }
    }

    public bool IsMovingNotifications => _isMovingNotifications;

    private void MoveDownNotification(int startIdx, float heightOffset) {
        for (int i = startIdx + 1; i < this.transform.childCount; i++) {
            NotificationUI notification = this.transform.GetChild(i).GetComponent<NotificationUI>();
            notification.RectTransform.DOComplete();
            notification.RectTransform
                .DOAnchorPosY(notification.RectTransform.anchoredPosition.y - heightOffset, .3f)
                .OnComplete(() => this._isMovingNotifications = false);
        }
    }
}

public enum NotificationType {
    BANK,
    HOSPITAL
}