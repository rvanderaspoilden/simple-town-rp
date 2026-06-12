using System.Collections.Generic;
using Sim.Constellation.Branches;
using UnityEngine;

namespace Sim.Constellation {
    // Une « couche » d'affichage de la constellation. Le graphe entier est découpé en
    // couches indépendantes : une couche GLOBALE (centrée sur « Mon Broz », montrant la
    // base de chaque branche) plus une couche par (sous-)branche, centrée sur le nœud
    // base de cette branche et ne montrant que les nœuds qui lui appartiennent.
    //
    // Un nœud base d'une sous-couche apparaît comme PORTAIL dans la couche parente :
    // cliquer dessus ouvre sa propre couche. C'est la navigation « cliquer un nœud
    // racine ouvre le layer associé ».
    public class ConstellationLayer {
        public string key;                 // = baseNodeId (identifiant stable de la couche)
        public string baseNodeId;          // nœud affiché au centre de la couche
        public bool isGlobal;              // couche centrée sur « Mon Broz »
        public bool isTopLevel;            // couche racine (global + branches racines) → onglet
        public string displayName;
        public Color color = Color.white;
        public string parentLayerKey;      // couche à remonter (null pour la globale)

        // Tous les nœuds rendus dans cette couche (inclut le nœud base lui-même).
        public readonly List<string> memberNodeIds = new List<string>();
        // Sous-ensemble des membres qui sont des bases de sous-couches : cliquer dessus
        // descend dans la couche correspondante.
        public readonly HashSet<string> portalNodeIds = new HashSet<string>();
    }

    // Construit la liste des couches à partir du graphe. Lecture seule : ne touche pas
    // l'état ni les positions des nœuds (le layout par couche est calculé ailleurs).
    public static class ConstellationLayerModel {

        public static List<ConstellationLayer> Build(ConstellationGraphConfig graph) {
            var layers = new List<ConstellationLayer>();
            if (graph == null || graph.nodes == null || graph.nodes.Count == 0) return layers;

            var center = graph.GetNode(graph.centerNodeId) ?? graph.nodes[0];
            if (center == null) return layers;

            // Arbre couvrant partagé (même source que l'état et l'ancien layout).
            ConstellationTreeLayout.BuildSpanningTree(graph, out var parent, out _, out var children);

            // Ensemble des nœuds « base » qui démarrent leur propre couche :
            //  - les enfants directs du centre (bases de branches racines),
            //  - tout nœud qui DÉFINIT une (sous-)branche via definesBranch.
            // Le centre lui-même reste dans baseIds (sert de borne d'arrêt / détection de
            // portail) mais NE produit PAS de couche : la « vue globale » a été retirée,
            // les onglets de branches couvrant déjà la navigation.
            var baseIds = new HashSet<string> { center.id };
            if (children.TryGetValue(center.id, out var rootBases))
                foreach (var id in rootBases) baseIds.Add(id);
            foreach (var n in graph.nodes)
                if (n != null && n.definesBranch != null && !string.IsNullOrEmpty(n.id)) baseIds.Add(n.id);

            // Ordre déterministe des couches : bases de branches racines (ordre des enfants
            // du centre) puis sous-branches (ordre des nœuds du graphe). Le centre est exclu.
            var orderedBases = new List<string>();
            void AddBase(string id) {
                if (!string.IsNullOrEmpty(id) && id != center.id
                    && baseIds.Contains(id) && !orderedBases.Contains(id)) orderedBases.Add(id);
            }
            if (children.TryGetValue(center.id, out var rb)) foreach (var id in rb) AddBase(id);
            foreach (var n in graph.nodes) if (n != null && n.definesBranch != null) AddBase(n.id);

            foreach (var baseId in orderedBases) {
                var baseNode = graph.GetNode(baseId);
                if (baseNode == null) continue;

                var layer = new ConstellationLayer {
                    key = baseId,
                    baseNodeId = baseId,
                    isGlobal = false,
                };

                // Niveau et parent de la couche. Un ancêtre == centre signifie « racine » :
                // pas de couche parente (le centre n'en a pas).
                string p = parent.TryGetValue(baseId, out var pp) ? pp : null;
                layer.isTopLevel = p == center.id;
                string ancestor = p;
                int guard = 0;
                while (ancestor != null && !baseIds.Contains(ancestor) && guard++ < 256)
                    ancestor = parent.TryGetValue(ancestor, out var a) ? a : null;
                layer.parentLayerKey = ancestor == center.id ? null : ancestor;

                // Libellé / couleur depuis la branche définie (sinon la branche d'affichage).
                {
                    var b = baseNode.definesBranch != null ? baseNode.definesBranch : baseNode.branch;
                    layer.displayName = b != null && !string.IsNullOrEmpty(b.displayName) ? b.displayName
                                       : (b != null ? b.id : baseNode.displayName);
                    layer.color = b != null ? b.color : Color.white;
                }

                // Membres : base + descendants de l'arbre couvrant, en s'arrêtant aux
                // autres bases (qui deviennent des portails, non parcourus).
                layer.memberNodeIds.Add(baseId);
                var stack = new Stack<string>();
                if (children.TryGetValue(baseId, out var directKids))
                    foreach (var k in directKids) stack.Push(k);
                while (stack.Count > 0) {
                    string id = stack.Pop();
                    layer.memberNodeIds.Add(id);
                    if (baseIds.Contains(id)) {
                        // Base d'une sous-couche : portail, on ne descend pas dedans.
                        layer.portalNodeIds.Add(id);
                        continue;
                    }
                    if (children.TryGetValue(id, out var kids))
                        foreach (var k in kids) stack.Push(k);
                }

                layers.Add(layer);
            }

            return layers;
        }
    }
}
