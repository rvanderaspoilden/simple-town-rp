using System;
using System.Collections.Generic;
using Sim;
using Sim.Entities.Persistence;
using UnityEngine;

namespace Sim.Constellation {
    // Phase 2 provider. Mirrors MockConstellationDataProvider's surface
    // (IConstellationDataProvider) but every state mutation goes through the
    // backend REST chokepoint. The local in-memory ConstellationState is
    // hydrated on construction and re-applied on every successful response —
    // it stays a mirror of the persisted truth, never the authority.
    //
    // Cost validation is duplicated client + server : the UI button checks
    // CanAfford locally to avoid noisy requests, and the server re-validates
    // the wallet on debit to defeat tampering.
    public class BackendConstellationDataProvider : IConstellationDataProvider {
        public ConstellationGraphConfig Graph { get; }
        public ConstellationState State { get; }

        public event Action<ConstellationNodeData> OnNodeUnlocked;
        public event Action OnStateChanged;

        private readonly string _characterId;
        private bool _hydrated;

        public BackendConstellationDataProvider(string characterId) {
            _characterId = characterId;
            Graph = Resources.Load<ConstellationGraphConfig>("Configurations/Constellation/ConstellationGraph");
            if (Graph == null) Graph = ConstellationGraphConfig.CreateDefault();
            State = new ConstellationState(Graph);

            // Hydration is async ; until OnConstellationRetrieved fires the
            // wallet is empty + nothing is unlocked. Listening callers should
            // subscribe to OnStateChanged before opening the UI so they
            // re-render when the snapshot lands.
            ApiManager.OnConstellationRetrieved += ApplySnapshot;
            ApiManager.OnConstellationUpdated   += ApplySnapshot;
            ApiManager.Instance?.RetrieveConstellation(_characterId);
        }

        // ── Hydration ──────────────────────────────────────────────────────

        // Replace the in-memory wallets and unlocked set with the snapshot
        // returned by the backend. Applied after every successful request
        // (retrieve / unlock / grant).
        private void ApplySnapshot(ConstellationStateData snapshot) {
            if (snapshot == null) return;

            // Reset the wallets to match the snapshot (full overwrite — the
            // server always returns the authoritative values).
            SetWalletFromSnapshot(snapshot);

            // Reapply unlocked set ; ForceUnlock is additive only, so we don't
            // need a removal pass (unlocks aren't reversible).
            if (snapshot.unlocked_node_ids != null) {
                foreach (var id in snapshot.unlocked_node_ids) State.ForceUnlock(id);
            }
            // Defensive : ensure every default-unlocked node is in the local set
            // even if the server snapshot is stale (e.g. a character row created
            // before migration 25 was applied). The server-side trigger normally
            // takes care of this, but the client can't trust an old row.
            foreach (var id in MockConstellationDataProvider.DefaultUnlockedNodeIds) State.ForceUnlock(id);
            if (!string.IsNullOrEmpty(snapshot.last_discovered_node_id)) {
                State.LastDiscoveredNodeId = snapshot.last_discovered_node_id;
            }

            _hydrated = true;
            OnStateChanged?.Invoke();
        }

        private void SetWalletFromSnapshot(ConstellationStateData snapshot) {
            // Devise unique : tableau sparse keyé par BranchConfig.id (racines ET
            // sous-branches). Seules les devises jamais touchées reviennent ; les
            // autres restent à 0 (valeur par défaut en ConstellationState).
            if (snapshot.points != null) {
                foreach (var entry in snapshot.points) {
                    if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                    SetExact(entry.id, entry.points);
                }
            }
        }

        // ConstellationState only exposes additive AddAvailable mutators ; for a
        // snapshot we want an exact set → compute the delta to land at target.
        private void SetExact(string branchId, int target) {
            int delta = target - State.GetAvailable(branchId);
            if (delta != 0) State.AddAvailable(branchId, delta);
        }

        // ── IConstellationDataProvider ─────────────────────────────────────

        public void AddPoints(string branchId, int amount) {
            if (string.IsNullOrEmpty(branchId)) return;
            var body = new GrantPointsBody {
                points = new Dictionary<string, int> { { branchId, amount } }
            };
            ApiManager.Instance?.GrantConstellationPoints(_characterId, body);
        }

        public bool TryUnlock(ConstellationNodeData node) {
            if (node == null) return false;
            // Local CanUnlock check is preserved for two reasons :
            //  1. it avoids round-trips when the UI clearly shouldn't have
            //     enabled the button in the first place ;
            //  2. extra-prerequisite (multi-parent) check stays client-side
            //     since the server doesn't know the graph.
            if (!State.CanUnlock(node)) return false;

            // Build the FLAT cost map from the node's cost list (single source of
            // truth). Key = BranchConfig.id (keyspace unique). Cumule si plusieurs
            // entrées partagent la même devise.
            var costs = new Dictionary<string, int>();
            foreach (var e in State.CostsOf(node)) {
                costs.TryGetValue(e.branch.id, out int cur);
                costs[e.branch.id] = cur + e.amount;
            }

            var body = new UnlockNodeBody { nodeId = node.id, costs = costs };

            // Fire-and-forget : the response will land via OnConstellationUpdated
            // and replay through ApplySnapshot. We also raise OnNodeUnlocked
            // (which drives the absorption pop + fly anim completion) on
            // success.
            ApiManager.Instance?.UnlockConstellationNode(_characterId, body,
                onSuccess: snapshot => OnNodeUnlocked?.Invoke(node),
                onError:   reason   => {
                    // Le serveur a refusé (fonds insuffisants ou autre). On lève
                    // OnStateChanged manuellement : l'UI ré-évalue la solvabilité,
                    // restaure les compteurs autoritatifs (les tweens d'anim seront
                    // tués par Refresh) et lève le lock global des boutons.
                    Debug.LogWarning("[Constellation] Unlock rejected by server: " + reason);
                    OnStateChanged?.Invoke();
                });

            // Note : because the call is async, the unlock is not "applied"
            // yet by the time we return here. The fly burst in UnlockUI
            // already plays optimistically ; provider-driven side effects
            // (animation pop) wait for the OnNodeUnlocked callback above.
            return true;
        }

        // Detach when the provider goes away (e.g. session end).
        public void Dispose() {
            ApiManager.OnConstellationRetrieved -= ApplySnapshot;
            ApiManager.OnConstellationUpdated   -= ApplySnapshot;
        }
    }
}
