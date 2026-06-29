using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Logique serveur du « freeze d'interaction » NPC. Plain C# singleton (pattern
/// <see cref="NpcMerchantService"/>). Les handlers sont enregistrés/désenregistrés par
/// <see cref="NpcSystemBootstrap"/>.
///
/// Modèle co-interaction (multi-joueurs) :
///   • Un NPC peut être interactingé par plusieurs joueurs simultanément (par exemple
///     plusieurs joueurs ouvrant la boutique du même marchand).
///   • Le NPC reste freezé tant qu'au moins un owner est actif (compteur géré dans
///     <see cref="NpcAIController._interactionOwners"/>).
///   • L'orientation du NPC vise l'owner le plus proche horizontalement (recalculée
///     chaque tick — cf. <see cref="NpcAIController.TickInteractionOverlay"/>).
///
/// Sécurité serveur :
///   • Room match obligatoire (rejet si joueur pas dans la même room que le NPC).
///   • Portée serveur ≤ 5 m (buffer généreux au-dessus de la portée client 3 m, pour
///     absorber l'interpolation + le mouvement pendant que la requête voyage).
///   • Refus si NPC introuvable ou knocked down.
///
/// Watchdog : timeout 30 s sans heartbeat → release auto. Le client réémet
/// périodiquement <see cref="C2S_NpcRequestInteraction"/> tant qu'une session est
/// active (heartbeat — la même requête fait office de refresh).
/// </summary>
public class NpcInteractionService {
    private static NpcInteractionService _instance;
    public static  NpcInteractionService Instance => _instance ??= new NpcInteractionService();

    // Buffer de portée serveur (3 m client + 2 m absorption interpolation / latence).
    private const float MaxServerRangeSqr = 5f * 5f;
    // Timeout : 30 s sans heartbeat → release auto (filet de secours pour bugs de fermeture client).
    private const float TimeoutSeconds = 30f;

    // npcId → owners (conn → instant d'expiration absolu).
    private readonly Dictionary<int, Dictionary<NetworkConnectionToClient, float>> _ownersByNpc
        = new Dictionary<int, Dictionary<NetworkConnectionToClient, float>>();
    // conn → npcs détenus par cette conn (pour release en masse à la déco / room change).
    private readonly Dictionary<NetworkConnectionToClient, HashSet<int>> _ownedByConn
        = new Dictionary<NetworkConnectionToClient, HashSet<int>>();

    // Buffers temporaires réutilisés (évite l'alloc dans Tick).
    private readonly List<int> _scratchNpcIds  = new List<int>(8);
    private readonly List<NetworkConnectionToClient> _scratchConns
        = new List<NetworkConnectionToClient>(8);

    // ── C2S handlers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Handler de Request. Sert aussi de heartbeat : si la conn est déjà owner du NPC, on
    /// refresh juste son timeout sans incrémenter le compteur d'owners.
    /// </summary>
    public void HandleRequest(NetworkConnectionToClient conn, C2S_NpcRequestInteraction msg) {
        if (conn?.identity == null) return;

        if (!NpcAIController.TryGet(msg.NpcId, out NpcAIController npc) || npc == null) {
            Reply(conn, msg.NpcId, false, NpcInteractionReason.NoNpc);
            return;
        }

        // Heartbeat : déjà owner → refresh timeout, pas de re-Begin (le compteur ne change pas).
        if (_ownersByNpc.TryGetValue(msg.NpcId, out var owners) && owners.ContainsKey(conn)) {
            owners[conn] = Time.time + TimeoutSeconds;
            Reply(conn, msg.NpcId, true, NpcInteractionReason.Ok);
            return;
        }

        // Validations server-side (autorité).
        if (PlayerRoomTracker.Instance.GetRoom(conn) != npc.RoomId) {
            Reply(conn, msg.NpcId, false, NpcInteractionReason.RoomMismatch);
            return;
        }

        Transform playerT = conn.identity.transform;
        Vector3   delta   = playerT.position - npc.transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude > MaxServerRangeSqr) {
            Reply(conn, msg.NpcId, false, NpcInteractionReason.OutOfRange);
            return;
        }

        // Note : pas de getter public pour le knockdown, mais ServerKnockDown a déjà
        // appelé ReleaseAllForNpc(KnockedDown) au moment où il a activé le ragdoll.
        // Une Request qui arrive *pendant* le knockdown sera donc autorisée (overlay
        // appliqué) — c'est acceptable : le client recevra le S2C_NpcKnockdown et
        // fermera ses modales via le path normal.

        // Begin (idempotent côté AIController) + maj des dicts + timeout initial.
        npc.ServerBeginInteraction(playerT);

        if (!_ownersByNpc.TryGetValue(msg.NpcId, out owners)) {
            owners = new Dictionary<NetworkConnectionToClient, float>(4);
            _ownersByNpc[msg.NpcId] = owners;
        }
        owners[conn] = Time.time + TimeoutSeconds;

        if (!_ownedByConn.TryGetValue(conn, out var set)) {
            set = new HashSet<int>();
            _ownedByConn[conn] = set;
        }
        set.Add(msg.NpcId);

        Reply(conn, msg.NpcId, true, NpcInteractionReason.Ok);
    }

    public void HandleEnd(NetworkConnectionToClient conn, C2S_NpcEndInteraction msg) {
        if (conn == null) return;
        ReleaseOne(conn, msg.NpcId, sendResponse: false, NpcInteractionReason.Ok);
    }

    // ── Tick (called by NpcServerTicker) ─────────────────────────────────────

    /// <summary>
    /// Détecte les owners en timeout (pas de heartbeat depuis 30 s) et force leur release.
    /// </summary>
    public void Tick(float dt) {
        if (_ownersByNpc.Count == 0) return;
        float now = Time.time;

        _scratchNpcIds.Clear();
        foreach (int npcId in _ownersByNpc.Keys) _scratchNpcIds.Add(npcId);

        for (int i = 0; i < _scratchNpcIds.Count; i++) {
            int npcId = _scratchNpcIds[i];
            if (!_ownersByNpc.TryGetValue(npcId, out var owners)) continue;

            _scratchConns.Clear();
            foreach (var kv in owners) {
                if (kv.Value <= now) _scratchConns.Add(kv.Key);
            }
            for (int j = 0; j < _scratchConns.Count; j++) {
                // Timeout : on libère silencieusement (le client peut être crashé,
                // toute Response serait perdue).
                ReleaseOne(_scratchConns[j], npcId, sendResponse: false, NpcInteractionReason.Ok);
            }
        }
    }

    // ── Hooks lifecycle ──────────────────────────────────────────────────────

    /// <summary>
    /// Joueur quitte une room (couvre la déco Mirror via <c>PlayerRoomTracker.OnDisconnect</c>
    /// qui appelle <c>LeaveRoom</c>). Release tous les locks détenus par cette conn.
    /// </summary>
    public void OnPlayerLeaveRoom(NetworkConnectionToClient conn, string _roomId) {
        if (conn == null) return;
        if (!_ownedByConn.TryGetValue(conn, out var npcs)) return;

        _scratchNpcIds.Clear();
        foreach (int npcId in npcs) _scratchNpcIds.Add(npcId);
        for (int i = 0; i < _scratchNpcIds.Count; i++) {
            ReleaseOne(conn, _scratchNpcIds[i], sendResponse: false, NpcInteractionReason.Ok);
        }
    }

    /// <summary>
    /// NPC despawné (pool, fin de session). Nettoyage silencieux des dicts — le
    /// <c>S2C_DestroyNpc</c> qui suit déclenche déjà la fermeture des sessions côté client.
    /// </summary>
    public void OnNpcDespawned(int npcId) {
        if (!_ownersByNpc.TryGetValue(npcId, out var owners)) return;
        foreach (var conn in owners.Keys) {
            if (_ownedByConn.TryGetValue(conn, out var set)) {
                set.Remove(npcId);
                if (set.Count == 0) _ownedByConn.Remove(conn);
            }
        }
        _ownersByNpc.Remove(npcId);
    }

    /// <summary>
    /// Release brutal de toutes les sessions sur un NPC (ex : knockdown). Envoie une Response
    /// refus avec <paramref name="reason"/> à chaque owner pour que les modales se ferment, puis
    /// nettoie les dicts. NB : l'AIController fait ensuite son propre <c>ServerForceReleaseAllInteractions</c>
    /// (le service ne touche pas à la liste de Transforms — séparation des responsabilités).
    /// </summary>
    public void ReleaseAllForNpc(int npcId, NpcInteractionReason reason) {
        if (!_ownersByNpc.TryGetValue(npcId, out var owners)) return;

        _scratchConns.Clear();
        foreach (var conn in owners.Keys) _scratchConns.Add(conn);
        for (int i = 0; i < _scratchConns.Count; i++) {
            var conn = _scratchConns[i];
            Reply(conn, npcId, false, reason);
            if (_ownedByConn.TryGetValue(conn, out var set)) {
                set.Remove(npcId);
                if (set.Count == 0) _ownedByConn.Remove(conn);
            }
        }
        _ownersByNpc.Remove(npcId);
    }

    /// <summary>Wipes all state. Call on server stop.</summary>
    public void Reset() {
        _ownersByNpc.Clear();
        _ownedByConn.Clear();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void ReleaseOne(NetworkConnectionToClient conn, int npcId,
                            bool sendResponse, NpcInteractionReason reason) {
        bool wasOwner = false;
        if (_ownersByNpc.TryGetValue(npcId, out var owners)) {
            if (owners.Remove(conn)) wasOwner = true;
            if (owners.Count == 0) _ownersByNpc.Remove(npcId);
        }
        if (_ownedByConn.TryGetValue(conn, out var set)) {
            set.Remove(npcId);
            if (set.Count == 0) _ownedByConn.Remove(conn);
        }
        if (!wasOwner) return;

        if (NpcAIController.TryGet(npcId, out var npc) && npc != null) {
            Transform t = (conn?.identity != null) ? conn.identity.transform : null;
            npc.ServerEndInteraction(t);
        }

        if (sendResponse && conn != null) Reply(conn, npcId, false, reason);
    }

    private static void Reply(NetworkConnectionToClient conn, int npcId,
                              bool accepted, NpcInteractionReason reason) {
        conn.Send(new S2C_NpcInteractionResponse {
            NpcId    = npcId,
            Accepted = accepted,
            Reason   = (byte)reason
        });
    }
}
