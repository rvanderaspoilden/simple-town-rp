using System.Collections;
using Mirror;
using Sim;
using Sim.Entities;
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
}
