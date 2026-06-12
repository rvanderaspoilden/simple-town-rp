using System;
using System.Collections.Generic;
using Sim.Constellation.Branches;
using UnityEngine;

namespace Sim.Constellation {
    public enum NodeState { Locked, Unlocked, Mastered }

    // Interface du fournisseur de données de la constellation.
    // Phase 1 : MockConstellationDataProvider (local). Phase 2 :
    // BackendConstellationDataProvider (REST + SyncVar).
    public interface IConstellationDataProvider {
        ConstellationGraphConfig Graph { get; }
        ConstellationState State { get; }

        event Action<ConstellationNodeData> OnNodeUnlocked;
        event Action OnStateChanged;

        // Crédite des points dépensables sur une devise (branche racine OU sous-branche),
        // identifiée par sa clé canonique (BranchConfig.id). Keyspace unique.
        void AddPoints(string branchId, int amount);

        // Tente de dépenser les points pour débloquer un nœud.
        bool TryUnlock(ConstellationNodeData node);
    }

    // État runtime de la constellation. Modèle = DÉPENSE ACTIVE : le joueur accumule des
    // points dépensables par devise (_available, keyé par BranchConfig.id — racines ET
    // sous-branches dans un seul keyspace) et les dépense pour débloquer les nœuds.
    // Un nœud est dépensable (CanUnlock) si son PARENT d'arbre + SES EXTRA-PRÉREQUIS sont
    // débloqués ET que le joueur a assez de points sur CHAQUE devise de son `cost`.
    public class ConstellationState {
        private readonly ConstellationGraphConfig _graph;
        private readonly Dictionary<string, int> _available = new Dictionary<string, int>();
        private readonly HashSet<string> _unlocked = new HashSet<string>();
        private readonly Dictionary<string, string> _parent;

        public string LastDiscoveredNodeId { get; set; }

        public ConstellationState(ConstellationGraphConfig graph) {
            _graph = graph;
            foreach (var b in BranchDatabase.All)
                if (b != null && !string.IsNullOrEmpty(b.id)) _available[b.id] = 0;
            ConstellationTreeLayout.BuildSpanningTree(graph, out var parent, out _, out _);
            _parent = parent;
        }

        // ── Points dépensables (devise unique keyée par id) ────────────────
        public int GetAvailable(string branchId) =>
            !string.IsNullOrEmpty(branchId) && _available.TryGetValue(branchId, out int v) ? v : 0;
        public int GetAvailable(BranchConfig branch) => branch == null ? 0 : GetAvailable(branch.id);

        public void AddAvailable(string branchId, int amount) {
            if (string.IsNullOrEmpty(branchId)) return;
            _available[branchId] = Mathf.Max(0, (_available.TryGetValue(branchId, out int v) ? v : 0) + amount);
        }
        public void AddAvailable(BranchConfig branch, int amount) { if (branch != null) AddAvailable(branch.id, amount); }

        // Nœuds racines de (sous-)branche actuellement débloqués → leur devise est visible.
        public IEnumerable<ConstellationNodeData> ActiveBranchRoots() {
            foreach (var n in _graph.nodes) {
                if (n != null && n.definesBranch != null && IsUnlocked(n))
                    yield return n;
            }
        }

        public int AvailableTotal() {
            int t = 0;
            foreach (var kv in _available) t += kv.Value;
            return t;
        }

        // ── État de déblocage ──────────────────────────────────────────────
        public NodeState GetNodeState(ConstellationNodeData node) {
            if (node == null) return NodeState.Locked;
            if (node.isCenter) return NodeState.Mastered;
            return _unlocked.Contains(node.id) ? NodeState.Unlocked : NodeState.Locked;
        }

        public bool IsUnlocked(ConstellationNodeData node) =>
            node != null && (node.isCenter || _unlocked.Contains(node.id));
        public bool IsUnlocked(string id) => IsUnlocked(_graph.GetNode(id));

        public string GetParentId(string id) => _parent != null && _parent.TryGetValue(id, out var p) ? p : null;

        public bool IsParentUnlocked(ConstellationNodeData node) {
            if (node == null) return false;
            var pid = GetParentId(node.id);
            if (string.IsNullOrEmpty(pid)) return true;
            return IsUnlocked(pid);
        }

        public bool AreExtraPrereqsUnlocked(ConstellationNodeData node) {
            if (node == null) return true;
            foreach (var prereq in _graph.ResolveExtraPrereqs(node)) {
                if (!IsUnlocked(prereq)) return false;
            }
            return true;
        }

        // ── Coûts & dépense (liste agnostique) ─────────────────────────────
        // Entrées valides du coût d'un nœud (branche non-null, montant > 0).
        public IEnumerable<CostEntry> CostsOf(ConstellationNodeData node) {
            if (node == null || node.cost == null) yield break;
            for (int i = 0; i < node.cost.Count; i++) {
                var e = node.cost[i];
                if (e != null && e.branch != null && e.amount > 0) yield return e;
            }
        }

        public bool CanAfford(ConstellationNodeData node) {
            if (node == null) return false;
            foreach (var e in CostsOf(node))
                if (GetAvailable(e.branch.id) < e.amount) return false;
            return true;
        }

        public bool CanUnlock(ConstellationNodeData node) {
            if (node == null || node.isCenter) return false;
            if (IsUnlocked(node)) return false;
            if (!IsParentUnlocked(node)) return false;
            if (!AreExtraPrereqsUnlocked(node)) return false;
            return CanAfford(node);
        }

        public bool TryUnlock(ConstellationNodeData node) {
            if (!CanUnlock(node)) return false;
            foreach (var e in CostsOf(node)) AddAvailable(e.branch.id, -e.amount);
            _unlocked.Add(node.id);
            LastDiscoveredNodeId = node.id;
            return true;
        }

        // Déblocage gratuit (seed initial / hydratation Phase 2).
        public void ForceUnlock(string id) {
            if (!string.IsNullOrEmpty(id)) _unlocked.Add(id);
        }

        // Affordabilité dans [0,1] : min des ratios (dispo/coût) sur chaque devise du coût.
        // 1 si débloqué/centre/gratuit.
        public float GetUnlockProgress(ConstellationNodeData node) {
            if (node == null) return 0f;
            if (node.isCenter || IsUnlocked(node)) return 1f;
            float p = 1f;
            bool any = false;
            foreach (var e in CostsOf(node)) {
                any = true;
                p = Mathf.Min(p, Ratio(GetAvailable(e.branch.id), e.amount));
            }
            return any ? p : 1f;
        }

        private static float Ratio(int value, int threshold) {
            if (threshold <= 0) return 1f;
            float r = (float)value / threshold;
            return r < 0f ? 0f : (r > 1f ? 1f : r);
        }
    }
}
