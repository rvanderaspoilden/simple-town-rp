using System.Collections.Generic;
using UnityEngine;

namespace Sim.Constellation {
    // Disposition radiale d'UNE couche : le nœud base est au centre (0,0), ses enfants
    // directs sont répartis autour, et les descendants plus profonds rayonnent vers
    // l'extérieur dans le secteur de leur ancêtre direct. Comme chaque sous-arbre occupe
    // un secteur angulaire disjoint et que les liens sont des équerres, aucun lien ne
    // croise un autre lien ni un nœud — comme l'ancienne disposition globale, mais
    // restreinte aux membres de la couche.
    public static class ConstellationLayerLayout {
        public const float FirstRingDist = 340f;   // base → enfants directs
        public const float RankGap = 280f;          // distance entre rangs successifs
        public const float LaneGap = 240f;          // distance entre couloirs voisins

        public class Result {
            public readonly Dictionary<string, Vector2> positions = new Dictionary<string, Vector2>();
            // child → parent dans la couche (exclut le nœud base). Source des liens tracés.
            public readonly Dictionary<string, string> parentMap = new Dictionary<string, string>();
        }

        public static Result Apply(ConstellationGraphConfig graph, ConstellationLayer layer) {
            var result = new Result();
            if (graph == null || layer == null || layer.memberNodeIds.Count == 0) return result;

            var members = new HashSet<string>(layer.memberNodeIds);
            string baseId = layer.baseNodeId;

            // Adjacence non orientée restreinte aux membres.
            var adj = new Dictionary<string, List<string>>();
            void Link(string a, string b) {
                if (!adj.TryGetValue(a, out var la)) { la = new List<string>(); adj[a] = la; }
                if (!la.Contains(b)) la.Add(b);
            }
            foreach (var id in layer.memberNodeIds) {
                var node = graph.GetNode(id);
                if (node == null) continue;
                if (!adj.ContainsKey(id)) adj[id] = new List<string>();
                foreach (var other in graph.ResolveConnected(node)) {
                    if (other == null || string.IsNullOrEmpty(other.id) || other.id == id) continue;
                    if (!members.Contains(other.id)) continue;
                    Link(id, other.id);
                    Link(other.id, id);
                }
            }

            // BFS depuis la base → arbre couvrant restreint (parent / profondeur / enfants).
            var parent = new Dictionary<string, string>();
            var depth = new Dictionary<string, int> { [baseId] = 0 };
            var children = new Dictionary<string, List<string>>();
            var queue = new Queue<string>();
            queue.Enqueue(baseId);
            while (queue.Count > 0) {
                string cur = queue.Dequeue();
                if (!adj.TryGetValue(cur, out var nbs)) continue;
                foreach (var nb in nbs) {
                    if (depth.ContainsKey(nb)) continue;
                    depth[nb] = depth[cur] + 1;
                    parent[nb] = cur;
                    if (!children.TryGetValue(cur, out var kids)) { kids = new List<string>(); children[cur] = kids; }
                    kids.Add(nb);
                    queue.Enqueue(nb);
                }
            }
            foreach (var kv in parent) result.parentMap[kv.Key] = kv.Value;

            // Base au centre.
            result.positions[baseId] = Vector2.zero;
            if (!children.TryGetValue(baseId, out var topChildren) || topChildren.Count == 0)
                return result;

            // Angle de chaque enfant direct. Pour la couche globale, on garde les
            // directions cardinales historiques par branche (familiarité). Sinon,
            // répartition régulière sur 360° en partant du haut.
            int n = topChildren.Count;
            for (int i = 0; i < n; i++) {
                string childId = topChildren[i];
                var childNode = graph.GetNode(childId);
                float ang = layer.isGlobal && childNode != null
                    ? ConstellationTreeLayout.RootAngleDeg(childNode.branch)
                    : 90f - i * (360f / n);

                // Couloirs (post-ordre) du sous-arbre de cet enfant.
                var lane = new Dictionary<string, float>();
                int leaf = 0;
                AssignLane(childId, children, lane, ref leaf);
                float childLane = lane[childId];

                var stack = new Stack<string>();
                stack.Push(childId);
                while (stack.Count > 0) {
                    string id = stack.Pop();
                    int rank = depth[id] - 1;                 // l'enfant direct = rang 0
                    float l = lane[id] - childLane;           // recentre la branche sur son axe
                    result.positions[id] = DirPos(ang, FirstRingDist + rank * RankGap, l * LaneGap);
                    if (children.TryGetValue(id, out var kids))
                        foreach (var k in kids) stack.Push(k);
                }
            }

            return result;
        }

        private static float AssignLane(string id, Dictionary<string, List<string>> children,
                                        Dictionary<string, float> lane, ref int leaf) {
            if (!children.TryGetValue(id, out var kids) || kids.Count == 0) {
                lane[id] = leaf;
                leaf++;
                return lane[id];
            }
            float sum = 0f;
            foreach (var k in kids) sum += AssignLane(k, children, lane, ref leaf);
            lane[id] = sum / kids.Count;
            return lane[id];
        }

        private static Vector2 DirPos(float angleDeg, float along, float laneOffset) {
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 fwd = new Vector2(cos, sin);
            Vector2 side = new Vector2(-sin, cos);
            return fwd * along + side * laneOffset;
        }
    }
}
