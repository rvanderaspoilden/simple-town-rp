using UnityEngine;

public class TeleportPosition : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private string displayName;

    [Tooltip("Room ID sent in TeleportMessage.NewRoomId so ClientPropManager.EnterRoom() fires correctly. " +
             "Defaults to 'city' (outdoor zone). Override for indoor rooms (e.g. 'hall:Main:1').")]
    [SerializeField]
    private string roomId = "city";

    public Vector3 GetPosition() => this.transform.position;

    public string DisplayName => displayName;
    public string RoomId => roomId;
}