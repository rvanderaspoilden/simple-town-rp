#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sim.Constellation;
using Sim.Constellation.Branches;
using Sim.Professions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validation statique de tous les <see cref="ConstellationNodeData"/> + de chaque
/// <see cref="ConstellationGraphConfig"/>. Détecte les invariants cassés que ni le
/// compilateur ni le runtime (qui fait du best-effort) ne détecteraient :
///
///   - identité : id manquant, doublon ; nom d'asset incohérent avec l'id ;
///   - branches : ref SO manquante, hybride sans secondaryBranch, ou non-hybride avec ;
///   - connectivité : refs null, self-ref, doublons, bidirectionnalité cassée,
///     refs vers des nodes hors du graphe ;
///   - topologie : centerNodeId orphelin, graphe non connexe (composants isolés),
///     legacy IDs orphelins après migration ;
///   - métier : incohérences professionCost / professionCostConfig, racine métier
///     manquante pour une profession utilisée en cost ;
///   - seuils : valeurs négatives, mastery < unlock, etc.
///
/// Sortie : un rapport groupé errors / warnings dans une boîte de dialogue + dump
/// console détaillé. Aucun changement d'asset.
/// </summary>
internal static class ConstellationGraphValidator {

    [MenuItem("Tools/Constellation/Validate Graph")]
    public static void Validate() {
        var report = new ValidationReport();

        BranchDatabase.Reload();
        ProfessionDatabase.Reload();

        var graphs = LoadAllAssets<ConstellationGraphConfig>();
        if (graphs.Count == 0) {
            EditorUtility.DisplayDialog("Constellation Validator",
                "Aucun ConstellationGraphConfig trouvé en projet.", "OK");
            return;
        }

        var allNodes = LoadAllAssets<ConstellationNodeData>();

        // ── Identité globale (sur tous les assets, pas juste ceux dans un graph) ───
        ValidateAssetIds(allNodes, report);
        ValidateBranchAssets(report);

        foreach (var graph in graphs) {
            ValidateGraph(graph, report);
        }

        DumpToConsole(report);
        ShowDialog(report);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static void ValidateAssetIds(List<ConstellationNodeData> assets, ValidationReport r) {
        var idToAssets = new Dictionary<string, List<ConstellationNodeData>>();
        foreach (var node in assets) {
            if (node == null) continue;
            if (string.IsNullOrEmpty(node.id)) {
                r.Error(node, $"Asset '{AssetDatabase.GetAssetPath(node)}' has empty id.");
                continue;
            }
            if (string.IsNullOrEmpty(node.displayName))
                r.Warn(node, $"node '{node.id}': displayName is empty.");
            // Cohérence nom de fichier / id : helpful, pas bloquant.
            if (!string.Equals(node.name, node.id, System.StringComparison.Ordinal))
                r.Warn(node, $"node '{node.id}': asset name '{node.name}' differs from id.");
            if (!idToAssets.TryGetValue(node.id, out var bucket)) {
                bucket = new List<ConstellationNodeData>();
                idToAssets[node.id] = bucket;
            }
            bucket.Add(node);
        }
        foreach (var kv in idToAssets) {
            if (kv.Value.Count > 1) {
                var paths = string.Join(", ", kv.Value.Select(AssetDatabase.GetAssetPath));
                r.Error(kv.Value[0], $"Duplicate id '{kv.Key}' across assets: {paths}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validation globale des BranchConfig : keyspace unique → id non-vide + unique sur
    // TOUTES les branches (racines ET sous-branches), et chaîne `parent` sans cycle.
    private static void ValidateBranchAssets(ValidationReport r) {
        var byId = new Dictionary<string, BranchConfig>();
        foreach (var b in BranchDatabase.All) {
            if (b == null) continue;
            if (string.IsNullOrEmpty(b.id)) {
                r.Error(b, $"BranchConfig '{b.name}' has empty id. Run Generate Branch Assets.");
                continue;
            }
            if (byId.TryGetValue(b.id, out var other))
                r.Error(b, $"Duplicate branch id '{b.id}' on {b.name} and {other.name} (keyspace unique requis).");
            else
                byId[b.id] = b;

            // Cycle de parent.
            var seen = new HashSet<BranchConfig>();
            var cur = b;
            int guard = 0;
            while (cur != null && guard++ < 64) {
                if (!seen.Add(cur)) {
                    r.Error(b, $"BranchConfig '{b.id}' has a parent cycle.");
                    break;
                }
                cur = cur.parent;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static void ValidateGraph(ConstellationGraphConfig graph, ValidationReport r) {
        if (graph == null || graph.nodes == null) return;

        var ctx = $"graph '{graph.name}'";

        // Center.
        if (string.IsNullOrEmpty(graph.centerNodeId)) {
            r.Error(graph, $"{ctx}: centerNodeId is empty.");
        } else if (graph.GetNode(graph.centerNodeId) == null) {
            r.Error(graph, $"{ctx}: centerNodeId '{graph.centerNodeId}' does not resolve to a node in graph.nodes.");
        }

        // Index local des nodes du graphe pour les checks de connectivité.
        var inGraph = new HashSet<ConstellationNodeData>();
        foreach (var node in graph.nodes) {
            if (node == null) {
                r.Error(graph, $"{ctx}: graph.nodes contains a null entry.");
                continue;
            }
            inGraph.Add(node);
        }

        foreach (var node in graph.nodes) {
            if (node == null) continue;
            ValidateNodeBranchAndCost(node, r);
            ValidateConnections(node, inGraph, r);
        }

        ValidateBidirectional(graph, inGraph, r);
        ValidateConnectivity(graph, inGraph, r);
        ValidateBranchRoots(graph, r);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static void ValidateNodeBranchAndCost(ConstellationNodeData node, ValidationReport r) {
        if (node.isCenter) {
            if (node.cost != null && node.cost.Count > 0)
                r.Warn(node, $"node '{node.id}': isCenter but has a cost (center should be free).");
            return;
        }

        // Branche home (visuelle) : non-bloquant mais le nœud s'affichera sans couleur.
        if (node.branch == null)
            r.Warn(node, $"node '{node.id}': branch (home/visuel) is null — node will render uncolored.");

        // Coût : liste agnostique {branche, montant}.
        if (node.cost != null) {
            for (int i = 0; i < node.cost.Count; i++) {
                var e = node.cost[i];
                if (e == null || e.branch == null) {
                    r.Error(node, $"node '{node.id}': cost[{i}] has a null branch.");
                    continue;
                }
                if (e.amount <= 0)
                    r.Warn(node, $"node '{node.id}': cost[{i}] amount={e.amount} ≤ 0 — ignored at runtime.");
                if (string.IsNullOrEmpty(e.branch.id))
                    r.Error(node, $"node '{node.id}': cost[{i}] branch '{e.branch.name}' has empty id.");
                else if (BranchDatabase.ById(e.branch.id) == null)
                    r.Error(node, $"node '{node.id}': cost[{i}] branch id '{e.branch.id}' not found in BranchDatabase.");
            }
        }
    }

    private static void ValidateConnections(ConstellationNodeData node, HashSet<ConstellationNodeData> inGraph, ValidationReport r) {
        if (node.connectedNodes != null) {
            var seen = new HashSet<ConstellationNodeData>();
            for (int i = 0; i < node.connectedNodes.Length; i++) {
                var target = node.connectedNodes[i];
                if (target == null) {
                    r.Error(node, $"node '{node.id}': connectedNodes[{i}] is null.");
                    continue;
                }
                if (target == node)
                    r.Error(node, $"node '{node.id}': connectedNodes[{i}] is self-ref.");
                if (!seen.Add(target))
                    r.Warn(node, $"node '{node.id}': connectedNodes[{i}]='{target.id}' is a duplicate.");
                if (!inGraph.Contains(target))
                    r.Error(node, $"node '{node.id}': connectedNodes[{i}]='{target.id}' is NOT in graph.nodes.");
            }
        }

        if (node.extraPrerequisites != null) {
            var seen = new HashSet<ConstellationNodeData>();
            for (int i = 0; i < node.extraPrerequisites.Length; i++) {
                var target = node.extraPrerequisites[i];
                if (target == null) {
                    r.Error(node, $"node '{node.id}': extraPrerequisites[{i}] is null.");
                    continue;
                }
                if (target == node)
                    r.Error(node, $"node '{node.id}': extraPrerequisites[{i}] is self-ref.");
                if (!seen.Add(target))
                    r.Warn(node, $"node '{node.id}': extraPrerequisites[{i}]='{target.id}' is a duplicate.");
                if (!inGraph.Contains(target))
                    r.Error(node, $"node '{node.id}': extraPrerequisites[{i}]='{target.id}' is NOT in graph.nodes.");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static void ValidateBidirectional(ConstellationGraphConfig graph, HashSet<ConstellationNodeData> inGraph, ValidationReport r) {
        // Pour chaque arête A→B dans connectedNodes, vérifier qu'on a aussi B→A.
        // La convention dans CreateDefault est strictement bidirectionnelle.
        var adj = new Dictionary<ConstellationNodeData, HashSet<ConstellationNodeData>>();
        foreach (var node in graph.nodes) {
            if (node == null) continue;
            var set = new HashSet<ConstellationNodeData>();
            adj[node] = set;
            foreach (var target in graph.ResolveConnected(node)) {
                if (target == null) continue;
                set.Add(target);
            }
        }
        foreach (var kv in adj) {
            var a = kv.Key;
            foreach (var b in kv.Value) {
                if (!inGraph.Contains(b)) continue;
                if (!adj.TryGetValue(b, out var bSet) || !bSet.Contains(a))
                    r.Warn(a, $"node '{a.id}' → '{b.id}' is not bidirectional ('{b.id}' does not list '{a.id}' back).");
            }
        }
    }

    private static void ValidateConnectivity(ConstellationGraphConfig graph, HashSet<ConstellationNodeData> inGraph, ValidationReport r) {
        var center = graph.GetNode(graph.centerNodeId);
        if (center == null) return; // déjà loggé.

        var visited = new HashSet<ConstellationNodeData> { center };
        var queue = new Queue<ConstellationNodeData>();
        queue.Enqueue(center);
        while (queue.Count > 0) {
            var cur = queue.Dequeue();
            foreach (var nb in graph.ResolveConnected(cur)) {
                if (nb == null) continue;
                if (visited.Add(nb)) queue.Enqueue(nb);
            }
        }

        foreach (var node in inGraph) {
            if (!visited.Contains(node))
                r.Error(node, $"node '{node.id}' is UNREACHABLE from center '{graph.centerNodeId}' (isolated component).");
        }
    }

    private static void ValidateBranchRoots(ConstellationGraphConfig graph, ValidationReport r) {
        // Pour chaque SOUS-branche dépensée par un coût, on s'attend à ce qu'au moins un
        // node la déclare via definesBranch (sinon la devise se dépense mais aucun compteur
        // n'apparaît au profil). Les branches RACINES sont toujours affichées (showInProfile).
        var defined = new HashSet<string>();
        var costed = new HashSet<string>();
        foreach (var node in graph.nodes) {
            if (node == null) continue;
            if (node.definesBranch != null && !string.IsNullOrEmpty(node.definesBranch.id)) {
                defined.Add(node.definesBranch.id);
                if (node.definesBranch.parent == null)
                    r.Warn(node, $"node '{node.id}': definesBranch '{node.definesBranch.id}' is a ROOT branch (expected a sub-branch with a parent).");
            }
            if (node.cost != null) {
                foreach (var e in node.cost) {
                    if (e != null && e.branch != null && e.branch.parent != null && e.amount > 0 && !string.IsNullOrEmpty(e.branch.id))
                        costed.Add(e.branch.id);
                }
            }
        }
        foreach (var id in costed) {
            if (!defined.Contains(id))
                r.Warn(graph, $"graph '{graph.name}': sub-branch '{id}' has cost nodes but no definesBranch root. Counter won't appear in profile.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private class ValidationReport {
        public readonly List<(Object ctx, string msg)> errors = new();
        public readonly List<(Object ctx, string msg)> warnings = new();
        public void Error(Object ctx, string msg) => errors.Add((ctx, msg));
        public void Warn(Object ctx, string msg) => warnings.Add((ctx, msg));
    }

    private static void DumpToConsole(ValidationReport r) {
        foreach (var (ctx, msg) in r.errors) Debug.LogError($"[ConstellationValidator] {msg}", ctx);
        foreach (var (ctx, msg) in r.warnings) Debug.LogWarning($"[ConstellationValidator] {msg}", ctx);
        Debug.Log($"[ConstellationValidator] Done — {r.errors.Count} error(s), {r.warnings.Count} warning(s).");
    }

    private static void ShowDialog(ValidationReport r) {
        var sb = new StringBuilder();
        sb.AppendLine($"Errors: {r.errors.Count}");
        sb.AppendLine($"Warnings: {r.warnings.Count}");
        sb.AppendLine();
        if (r.errors.Count == 0 && r.warnings.Count == 0) {
            sb.AppendLine("Graph is clean.");
        } else {
            sb.AppendLine("Voir la console pour le détail (clic sur un message → ping de l'asset).");
        }
        EditorUtility.DisplayDialog("Constellation Validator", sb.ToString(), "OK");
    }

    private static List<T> LoadAllAssets<T>() where T : Object {
        var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        var list = new List<T>(guids.Length);
        foreach (var guid in guids) {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) list.Add(asset);
        }
        return list;
    }
}
#endif
