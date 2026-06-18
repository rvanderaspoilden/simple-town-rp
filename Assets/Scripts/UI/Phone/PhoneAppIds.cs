/// <summary>
/// Stable string ids for the phone apps. Used to:
///  - tag <see cref="PhoneApplicationUI"/> instances (single source of truth for icon + name),
///  - route notifications via <see cref="NotificationManager.AddNotification(string,string)"/>,
///  - serialize app identity over the wire (<c>ToastNotificationMessage.appId</c>).
///
/// Strings are intentional: backend / wire compatibility doesn't break when a
/// new app is added or an old one is removed.
/// </summary>
public static class PhoneAppIds {
    public const string Contacts = "contacts";
    public const string Career   = "career";
    public const string Bank     = "bank";
    public const string Shop     = "shop";
    public const string Support  = "support";
    public const string Settings = "settings";
    public const string Leave    = "leave";
}
