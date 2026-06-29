using Sim.Logging;
using Sim.UI;
using UnityEngine;

/// <summary>
/// Session client d'interaction avec un NPC : posée par <c>Begin</c> dès qu'on clique sur un NPC
/// (avant même l'arrivée à portée), levée par <c>End</c> à la fermeture de toute modale / radial /
/// abandon. Idempotente : appels multiples sans effet de bord.
///
/// Le joueur local ne peut tenir qu'UNE session active à la fois — un Begin sur un autre NPC
/// referme proprement la session courante.
///
/// Heartbeat : tant qu'une session est ouverte, on réémet périodiquement la Request (sert de
/// keepalive — voir <see cref="NpcInteractionService.TimeoutSeconds"/> côté serveur, 30 s). Couvre
/// le cas du dialogue qui dure : sans ça, le serveur release le NPC alors que le joueur lit.
/// </summary>
public static class NpcInteractionSession {
    /// <summary>NPC actuellement « interactingé » par le joueur local, ou null si aucun.</summary>
    public static int? ActiveNpcId { get; private set; }

    private const float HeartbeatIntervalSeconds = 10f;

    /// <summary>
    /// Ouvre une session sur ce NPC. Si une autre session est déjà ouverte, la ferme proprement
    /// (C2S End vers l'ancien, puis C2S Request vers le nouveau). Idempotent sur le même NPC :
    /// réémet une Request comme heartbeat (le service serveur traite ça comme un refresh).
    /// </summary>
    public static void Begin(ClientNpcView view) {
        if (view == null) return;
        int npcId = view.NpcId;

        if (ActiveNpcId.HasValue && ActiveNpcId.Value != npcId) {
            // Transition A → B : ferme proprement A avant d'ouvrir B.
            SendEnd(ActiveNpcId.Value);
            ActiveNpcId = null;
        }

        ActiveNpcId = npcId;
        SendRequest(npcId);
        HeartbeatPump.Ensure();
    }

    /// <summary>
    /// Ferme la session sur ce NPC. Idempotent : no-op si <paramref name="npcId"/> n'est pas
    /// l'active. Envoie C2S End au serveur (qui release le lock).
    /// </summary>
    public static void End(int npcId) {
        if (!ActiveNpcId.HasValue || ActiveNpcId.Value != npcId) return;
        SendEnd(npcId);
        ActiveNpcId = null;
    }

    /// <summary>
    /// Variante silencieuse : nettoie la session SANS envoyer de C2S. Utilisée quand le serveur a
    /// déjà cleané ses dicts (despawn NPC, refus de session).
    /// </summary>
    public static void EndSilent(int npcId) {
        if (!ActiveNpcId.HasValue || ActiveNpcId.Value != npcId) return;
        ActiveNpcId = null;
    }

    /// <summary>
    /// Câblé via <see cref="ClientNpcManager.OnNpcInteractionResponse"/> dans
    /// <see cref="NpcInteractionSessionBootstrap"/>. Refus → ferme modales + reset session.
    /// </summary>
    public static void HandleResponse(int npcId, bool accepted, byte reason) {
        if (accepted) return;

        // Refus (ou notification de fin forcée d'une session déjà ouverte, ex : knockdown du NPC).
        ClientLogger.Network("NpcInteractionRefused {NpcId} {Reason}", npcId, reason);
        WorldToastManager.ShowError(ReasonText(reason));
        ForceCloseSession(npcId);
    }

    /// <summary>NPC despawné localement : nettoyage silencieux + fermeture forcée des modales.</summary>
    public static void HandleNpcDestroyed(int npcId) {
        ForceCloseSession(npcId);
    }

    /// <summary>
    /// Ferme la session sur ce NPC + ferme les modales actives (dialogue/shop) si elles
    /// pointaient sur ce NPC. EndSilent pour éviter de renvoyer un C2S inutile (cas refus / despawn).
    /// </summary>
    private static void ForceCloseSession(int npcId) {
        EndSilent(npcId);
        if (DialogueUI.Instance != null && DialogueUI.Instance.gameObject.activeSelf) {
            DialogueUI.Instance.Hide();
        }
        if (MerchantShopUI.Instance != null && MerchantShopUI.Instance.gameObject.activeSelf) {
            MerchantShopUI.Instance.Hide();
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static void SendRequest(int npcId) {
        ClientNpcManager.Instance?.RequestNpcInteraction(npcId);
    }

    private static void SendEnd(int npcId) {
        ClientNpcManager.Instance?.EndNpcInteraction(npcId);
    }

    private static string ReasonText(byte reason) {
        switch ((NpcInteractionReason)reason) {
            case NpcInteractionReason.NoNpc:        return "Ce personnage n'est plus là.";
            case NpcInteractionReason.OutOfRange:   return "Tu es trop loin.";
            case NpcInteractionReason.KnockedDown:  return "Ce personnage est au sol.";
            case NpcInteractionReason.RoomMismatch: return "Tu n'es pas dans la bonne zone.";
            default:                                return "Interaction impossible.";
        }
    }

    // ── Heartbeat pump (autocréé au premier Begin) ─────────────────────────────

    private class HeartbeatPump : MonoBehaviour {
        private static HeartbeatPump _instance;
        public static void Ensure() {
            if (_instance != null) return;
            var go = new GameObject("NpcInteractionHeartbeat");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HeartbeatPump>();
        }
        private void Start() => InvokeRepeating(nameof(Beat), HeartbeatIntervalSeconds, HeartbeatIntervalSeconds);
        private void Beat() {
            if (ActiveNpcId.HasValue) SendRequest(ActiveNpcId.Value);
        }
    }
}

/// <summary>
/// Branche les events client (<see cref="ClientNpcManager.OnNpcInteractionResponse"/> et
/// <see cref="ClientNpcManager.OnNpcDestroyed"/>) sur la <see cref="NpcInteractionSession"/>.
/// Auto-instanciée à <c>RuntimeInitializeOnLoadMethod</c> pour s'abonner une seule fois,
/// indépendamment du cycle de vie de scène.
/// </summary>
internal static class NpcInteractionSessionBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init() {
        ClientNpcManager.OnNpcInteractionResponse += NpcInteractionSession.HandleResponse;
        ClientNpcManager.OnNpcDestroyed           += NpcInteractionSession.HandleNpcDestroyed;
    }
}
