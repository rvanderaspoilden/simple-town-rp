using System.Collections;
using Mirror;
using Sim;
using Sim.Building;
using Sim.Entities;
using Sim.Scriptables;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// MonoBehaviour singleton for prop interactions that require async HTTP calls.
/// Lives on the server; the router delegates here instead of blocking the message thread.
/// </summary>
public class PropInteractionDispatcher : MonoBehaviour {
    public static PropInteractionDispatcher Instance { get; private set; }

    private void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── DeliveryBox ───────────────────────────────────────────────────────────

    public void OpenDeliveryBox(NetworkConnectionToClient conn, int propId, string roomId) {
        PlayerController player = conn.identity?.GetComponent<PlayerController>();
        if (player == null) return;

        string characterId = player.CharacterData?.Id;
        if (string.IsNullOrEmpty(characterId)) return;

        StartCoroutine(FetchAndSendDeliveries(conn, propId, roomId, characterId));
    }

    private IEnumerator FetchAndSendDeliveries(
        NetworkConnectionToClient conn, int propId, string roomId, string characterId
    ) {
        UnityWebRequest req = ApiManager.Instance.RetrieveDeliveriesRequest(characterId);
        yield return req.SendWebRequest();

        Delivery[] deliveries;

        if (req.responseCode == 200) {
            DeliveryResponse response = JsonUtility.FromJson<DeliveryResponse>(req.downloadHandler.text);
            deliveries = response?.Deliveries != null
                ? System.Linq.Enumerable.ToArray(response.Deliveries)
                : System.Array.Empty<Delivery>();
        } else {
            Debug.LogWarning($"[PropInteractionDispatcher] DeliveryBox fetch failed ({req.responseCode}) for char {characterId}");
            deliveries = System.Array.Empty<Delivery>();
        }

        if (conn == null || !conn.isReady) yield break;

        conn.Send(new S2C_DeliveryBoxOpened {
            PropId     = propId,
            RoomId     = roomId,
            Deliveries = deliveries
        });
    }

    /// <summary>
    /// Server-side fetch of deliveries for a delivery box. Updates the prop state
    /// with the new delivery count (drives client visual). Used:
    ///   - on delivery box spawn (Init)
    ///   - after a build operation consumed a delivery
    /// </summary>
    public void RefreshDeliveryBoxCount(int propId, string roomId, string characterId) {
        StartCoroutine(FetchAndUpdateDeliveryCount(propId, roomId, characterId));
    }

    private IEnumerator FetchAndUpdateDeliveryCount(int propId, string roomId, string characterId) {
        UnityWebRequest req = ApiManager.Instance.RetrieveDeliveriesRequest(characterId);
        yield return req.SendWebRequest();

        uint count = 0;
        if (req.responseCode == 200) {
            DeliveryResponse response = JsonUtility.FromJson<DeliveryResponse>(req.downloadHandler.text);
            if (response?.Deliveries != null) count = (uint)response.Deliveries.Count;
        }

        if (!ServerPropManager.Instance.TryGetPropState(roomId, propId, out var state)) yield break;

        // Preserve header (built/preset) when updating the delivery count.
        PropStateHeader header = PropStateHeader.ReadFrom(state.Payload);
        byte[] newPayload = new DeliveryBoxState { Header = header, DeliveryCount = count }.Serialize();
        ServerPropManager.Instance.UpdatePropState(roomId, propId, newPayload);
    }

    // ── Build prop (delivery → spawn → save) ──────────────────────────────────

    public void BuildProp(NetworkConnectionToClient conn, C2S_BuildProp msg, ApartmentController apt) {
        StartCoroutine(BuildPropCoroutine(conn, msg, apt));
    }

    private IEnumerator BuildPropCoroutine(NetworkConnectionToClient conn, C2S_BuildProp msg, ApartmentController apt) {
        // 1. Fetch delivery to validate + get type/paint info from backend
        UnityWebRequest deleteReq = ApiManager.Instance.DeleteDeliveryRequest(new Delivery { _id = msg.DeliveryId });
        yield return deleteReq.SendWebRequest();

        if (deleteReq.responseCode != 200) {
            Debug.LogError($"[PropInteractionDispatcher] Delete delivery {msg.DeliveryId} failed ({deleteReq.responseCode})");
            conn.Send(new S2C_BuildAck { Success = false });
            yield break;
        }

        // 2. Build initial payload — header carries presetId + isBuilt
        PropsConfig config = DatabaseManager.PropsDatabase.GetPropsById(msg.PropConfigId);
        if (config == null) {
            Debug.LogError($"[PropInteractionDispatcher] Unknown PropsConfig id={msg.PropConfigId}");
            conn.Send(new S2C_BuildAck { Success = false });
            yield break;
        }

        bool isBuilt = !config.MustBeBuilt();
        PropStateHeader header = new PropStateHeader { IsBuilt = isBuilt, PresetId = msg.PresetId };

        byte[] initialPayload;
        if (msg.PaintConfigId >= 0) {
            // PaintBucket initial payload (PropType.PaintBucket)
            initialPayload = new PaintBucketState {
                Header        = header,
                PaintConfigId = msg.PaintConfigId,
                R = msg.ColorR, G = msg.ColorG, B = msg.ColorB
            }.Serialize();
        } else {
            // Generic init: header only — sources will use their own initial state if needed
            initialPayload = new GenericPropState { Header = header }.Serialize();
        }

        // 3. Spawn the prop in the apartment room
        int newPropId = ServerPropManager.Instance.SpawnProp(
            apt.RoomId, msg.PropConfigId, msg.Position, msg.Rotation, initialPayload
        );
        if (newPropId < 0) {
            conn.Send(new S2C_BuildAck { Success = false });
            yield break;
        }

        // 4. Reparent server-side instance under apartment props container (for hierarchy queries)
        GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(newPropId);
        if (go != null && apt.PropsContainer != null) {
            go.transform.SetParent(apt.PropsContainer);
            go.transform.position = msg.Position;
            go.transform.rotation = msg.Rotation;
        }

        // 5. Refresh delivery box count (the build consumed a delivery)
        if (msg.DeliveryBoxPropId > 0) {
            string tenantCharId = apt.TenantId;
            if (!string.IsNullOrEmpty(tenantCharId)) {
                RefreshDeliveryBoxCount(msg.DeliveryBoxPropId, apt.RoomId, tenantCharId);
            }
        }

        // 6. Persist the apartment
        yield return apt.StartCoroutine(apt.Save());

        // 7. Notify the requesting client
        if (conn != null && conn.isReady) {
            conn.Send(new S2C_BuildAck { Success = true });
        }
    }
}
