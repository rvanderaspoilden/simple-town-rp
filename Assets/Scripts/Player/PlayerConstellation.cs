using System.Collections.Generic;
using Mirror;
using Sim.Constellation;
using Sim.Entities.Persistence;
using UnityEngine;

namespace Sim.Player {
    /// <summary>
    /// Player-attached component owning the IConstellationDataProvider. Pulled
    /// out of <see cref="Sim.Constellation.ConstellationUI"/> so the
    /// RewardSystem (server-side, job completion path) can credit points
    /// directly on the player without going through any UI singleton.
    ///
    /// Lives on every player ; the local player creates a real provider in
    /// OnStartLocalPlayer, remote players get null (their constellation isn't
    /// visible to others). A `useMockProvider` toggle exists for offline
    /// editor iteration — it shortcuts the backend by spinning up the mock.
    /// </summary>
    public class PlayerConstellation : NetworkBehaviour {

        [Header("Provider")]
        [Tooltip("If checked, uses the in-memory mock instead of the backend " +
                 "bridge. Useful when iterating on the UI without a running " +
                 "API server.")]
        [SerializeField] private bool useMockProvider;

        public IConstellationDataProvider Provider { get; private set; }

        private Sim.PlayerController _playerController;

        // Server-only mirror of the player's unlocked node ids. Hydrated at connect
        // (SimpleTownNetwork.SetupCharacterCoroutineInner fetches /constellation) and
        // kept fresh by CmdNotifyNodeUnlocked from the owner client. Read by the
        // mission gate (MissionServerManager.TakeFromBoard) to authorize node-gated
        // missions without a per-take backend round-trip.
        private readonly HashSet<string> _serverUnlockedNodeIds = new HashSet<string>();

        private void Awake() {
            _playerController = GetComponent<Sim.PlayerController>();
        }

        public override void OnStartLocalPlayer() {
            base.OnStartLocalPlayer();
            string characterId = _playerController != null && _playerController.CharacterData != null
                ? _playerController.CharacterData.Id
                : null;

            if (useMockProvider || string.IsNullOrEmpty(characterId)) {
                if (string.IsNullOrEmpty(characterId)) {
                    Debug.LogWarning("[PlayerConstellation] No character id available, falling back to mock provider.");
                }
                Provider = new MockConstellationDataProvider();
            } else {
                Provider = new BackendConstellationDataProvider(characterId);
            }

            // Keep the server's unlocked-node cache fresh: when this (owner) client
            // unlocks a node, tell the server so node-gated missions become takeable
            // immediately without re-fetching the backend.
            if (Provider != null) Provider.OnNodeUnlocked += OnLocalNodeUnlocked;
        }

        private void OnDestroy() {
            if (Provider != null) Provider.OnNodeUnlocked -= OnLocalNodeUnlocked;
            if (Provider is BackendConstellationDataProvider backend) backend.Dispose();
        }

        // ── Server-side unlocked-node cache (mission gate) ─────────────────

        /// <summary>Overwrite the server cache from a backend snapshot (connect-time
        /// hydration). Server-only.</summary>
        [Server]
        public void ServerSetUnlockedNodes(IEnumerable<string> ids) {
            _serverUnlockedNodeIds.Clear();
            if (ids == null) return;
            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id)) _serverUnlockedNodeIds.Add(id);
        }

        /// <summary>True if the player has unlocked the given node (server cache).</summary>
        [Server]
        public bool ServerHasUnlockedNode(string nodeId) =>
            !string.IsNullOrEmpty(nodeId) && _serverUnlockedNodeIds.Contains(nodeId);

        private void OnLocalNodeUnlocked(ConstellationNodeData node) {
            if (node != null && !string.IsNullOrEmpty(node.id)) CmdNotifyNodeUnlocked(node.id);
        }

        // The backend already validated affordability + prerequisites on the real
        // unlock (POST /constellation/unlock) before OnNodeUnlocked fired, so this is
        // a cache-warm signal, not the authority. Worst-case tamper is a spoofed id,
        // acceptable for the POC.
        [Command]
        private void CmdNotifyNodeUnlocked(string nodeId) {
            if (!string.IsNullOrEmpty(nodeId)) _serverUnlockedNodeIds.Add(nodeId);
        }

        // ── Server-side reward entry point ─────────────────────────────────

        /// <summary>
        /// Called by the RewardSystem at job completion. Forwards to the
        /// provider, which routes through the REST chokepoint
        /// (POST /constellation/grant). The `reason` is logged for now (the
        /// constellation_states table doesn't carry an audit trail in Phase 2 ;
        /// add one later if needed).
        /// </summary>
        [Server]
        public void GrantPoints(Dictionary<string, int> points, string reason) {
            if (Provider == null) {
                // Likely cause: this PlayerConstellation belongs to a remote
                // client (Provider is built in OnStartLocalPlayer, which only
                // fires on the local client). On host playing solo, this
                // shouldn't happen ; if it does, OnStartLocalPlayer didn't run
                // or the character id wasn't set yet.
                Debug.LogWarning($"[PlayerConstellation] Grant ignored, no provider on netId={netId} ({reason})");
                return;
            }
            if (points == null) return;
            // Keyspace unique : chaque clé est un BranchConfig.id (branche racine OU
            // sous-branche). Le provider route vers la map `points` unique.
            foreach (var kv in points) {
                if (kv.Value == 0) continue;
                Provider.AddPoints(kv.Key, kv.Value);
            }
        }
    }
}
