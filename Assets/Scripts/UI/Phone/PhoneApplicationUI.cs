using UnityEngine;

/// <summary>
/// Base class for every phone app. Each app carries its own identity
/// (<see cref="AppId"/> / <see cref="DisplayName"/> / <see cref="Icon"/>),
/// which is the single source of truth for both the home screen button
/// and any notification surfaced for this app (see <see cref="NotificationManager"/>).
/// </summary>
public abstract class PhoneApplicationUI : MonoBehaviour {
    [Header("App identity")]
    [Tooltip("Stable string id used by notifications + wire protocol. " +
             "Match the constants in PhoneAppIds (contacts, career, bank, shop, support, settings, leave…).")]
    [SerializeField] private string appId;

    [Tooltip("Display name shown as the notification header. Also the app label.")]
    [SerializeField] private string displayName;

    [Tooltip("Sprite used for the home screen button + notification icon. " +
             "Authored once here; the PhoneApplicationButton mirrors it automatically.")]
    [SerializeField] private Sprite icon;

    public string AppId => appId;
    public string DisplayName => displayName;
    public Sprite Icon => icon;

    public abstract void Back();
}
