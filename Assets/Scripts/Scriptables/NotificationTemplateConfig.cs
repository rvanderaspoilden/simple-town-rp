using UnityEngine;

[CreateAssetMenu(fileName = "New Notification Template", menuName = "Configurations/Notification Template")]
public class NotificationTemplateConfig : ScriptableObject {
    [SerializeField]
    private NotificationType notificationType;

    [SerializeField]
    private string title;

    [SerializeField]
    private Sprite icon;

    public NotificationType NotificationType => notificationType;

    public string Title => title;

    public Sprite Icon => icon;
}