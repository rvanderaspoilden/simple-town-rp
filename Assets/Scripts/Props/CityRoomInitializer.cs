using Mirror;
using UnityEngine;

/// <summary>
/// Scans the City scene for all ServerPropSource components and records their
/// initial state in ServerPropManager under the "city" room.
///
/// No instantiation, no broadcast — clients have the GameObjects through the
/// scene load. The room snapshot will deliver the current state on EnterRoom.
/// </summary>
public class CityRoomInitializer : NetworkBehaviour {
    private const string CityRoomId = "city";

    public override void OnStartServer() {
        base.OnStartServer();

        int count = 0;
        foreach (ServerPropSource source in FindObjectsByType<ServerPropSource>(FindObjectsSortMode.None)) {
            if (source.RoomId != CityRoomId) continue;
            ServerPropManager.Instance.RegisterSceneProp(source);
            count++;
        }

        Debug.Log($"[CityRoomInitializer] City room registered ({count} scene props)");
    }
}
