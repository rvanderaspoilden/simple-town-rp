using Mirror;
using Sim;
using Sim.Scriptables;
using UnityEngine;

/// <summary>
/// Route les messages C2S_PropInteraction vers le handler serveur approprié.
/// Les handlers synchrones s'exécutent directement.
/// Les handlers asynchrones (HTTP) délèguent à PropInteractionDispatcher.
///
/// Pour ajouter un nouveau type de prop interactif :
///   1. Ajouter un case dans Route()
///   2. Écrire un HandleXxx() ou déléguer au dispatcher
/// </summary>
public static class PropInteractionRouter {
    public static void Route(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        switch (msg.Type) {
            case PropType.Seat:         HandleSeat        (conn, msg); break;
            case PropType.PaintBucket:  HandlePaintBucket (conn, msg); break;
            case PropType.Dispenser:    HandleDispenser   (conn, msg); break;
            case PropType.DeliveryBox:  HandleDeliveryBox (conn, msg); break;
            case PropType.Door:
                Debug.LogWarning($"[PropInteractionRouter] Rejected door interaction from conn={conn.connectionId} (doors are trigger-driven)");
                break;
            default:
                Debug.LogWarning($"[PropInteractionRouter] Unhandled PropType={msg.Type} from conn={conn.connectionId}");
                break;
        }
    }

    // ── Seat ──────────────────────────────────────────────────────────────────

    private static void HandleSeat(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var state)) {
            Debug.LogWarning($"[PropInteractionRouter] Seat {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        SeatState current = SeatState.Deserialize(state.Payload);
        uint      sender  = conn.identity.netId;

        if (SeatInteraction.IsRevokeRequest(msg.Payload)) {
            if (current.OccupantNetId != sender) return;
            ServerPropManager.Instance.UpdatePropState(msg.RoomId, msg.PropId,
                new SeatState { Header = current.Header, OccupantNetId = 0 }.Serialize());
        } else if (SeatInteraction.IsSitRequest(msg.Payload)) {
            if (current.IsOccupied) return;
            ServerPropManager.Instance.UpdatePropState(msg.RoomId, msg.PropId,
                new SeatState { Header = current.Header, OccupantNetId = sender }.Serialize());
        }
    }

    // ── PaintBucket ───────────────────────────────────────────────────────────
    // L'interaction OPEN est client-local (ouvre l'UI de peinture).
    // Le serveur ne reçoit une interaction que si le joueur applique une peinture.
    // Ici on laisse l'ApartmentController gérer l'application (code existant).

    private static void HandlePaintBucket(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out _)) {
            Debug.LogWarning($"[PropInteractionRouter] PaintBucket {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        // Ouverture de l'UI paint : aucun état serveur à modifier,
        // le comportement est entièrement client-local → le client ouvre l'UI directement.
        // Ce handler existe pour valider l'autorité si nécessaire à l'avenir.
        if (PaintBucketInteraction.IsOpenRequest(msg.Payload)) {
            Debug.Log($"[PropInteractionRouter] PaintBucket {msg.PropId} opened by conn={conn.connectionId}");
        }
    }

    // ── Dispenser ─────────────────────────────────────────────────────────────

    private static void HandleDispenser(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out var state)) {
            Debug.LogWarning($"[PropInteractionRouter] Dispenser {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        int itemId = DispenserInteraction.GetItemId(msg.Payload);
        if (itemId < 0) return;

        // Récupère la configuration du distributeur depuis la base de données
        // Le prefabId du state est l'ID string qui mappe sur le PropsConfig
        ItemConfig itemConfig = DatabaseManager.ItemConfigs.Find(x => x.ID == itemId);
        if (itemConfig == null) {
            Debug.LogWarning($"[PropInteractionRouter] Item {itemId} not found in database");
            conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
            return;
        }

        PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
        if (bank == null) return;


        /*
        ItemPrice itemPrice = ((DispenserConfiguration) itemConfig).ItemsToSell.Find(x => x.item.ID == itemId);
        if (bank.Money < itemPrice.price) {
            conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = false, ItemId = -1 });
            return;
        }

        bank.TakeMoney(itemPrice.price);
        UnityEngine.Object item = UnityEngine.Object.Instantiate(
            itemPrice.item.Prefab, conn.identity.transform.position, UnityEngine.Quaternion.identity
        );
        NetworkServer.Spawn(item as UnityEngine.GameObject, conn);

        conn.Send(new S2C_DispenserPurchaseResult { PropId = msg.PropId, Success = true, ItemId = itemId });
        */

        Debug.Log($"[PropInteractionRouter] Player {conn.connectionId} bought item {itemId} from dispenser {msg.PropId}");
    }

    // ── DeliveryBox ───────────────────────────────────────────────────────────
    // Délègue au dispatcher (MonoBehaviour) car la réponse nécessite un appel HTTP.

    private static void HandleDeliveryBox(NetworkConnectionToClient conn, C2S_PropInteraction msg) {
        if (!DeliveryBoxInteraction.IsOpenRequest(msg.Payload)) return;

        if (!ServerPropManager.Instance.TryGetPropState(msg.RoomId, msg.PropId, out _)) {
            Debug.LogWarning($"[PropInteractionRouter] DeliveryBox {msg.PropId} not found in room '{msg.RoomId}'");
            return;
        }

        PropInteractionDispatcher.Instance?.OpenDeliveryBox(conn, msg.PropId, msg.RoomId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ParseConfigId(string prefabId) {
        // Convention : prefabId = "dispenser_{configId}" ou directement l'ID en string
        if (int.TryParse(prefabId, out int id)) return id;
        var parts = prefabId.Split('_');
        if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out int tail)) return tail;
        return -1;
    }
}
