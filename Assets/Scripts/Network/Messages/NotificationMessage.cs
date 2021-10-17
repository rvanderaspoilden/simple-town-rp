using System;
using Mirror;

[Serializable]
public struct NotificationMessage : NetworkMessage {
    public NotificationCode code;
}

public enum NotificationCode : byte {
    ITEM_DESTROYED
}