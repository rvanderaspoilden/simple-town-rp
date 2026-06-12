using System;
using System.Collections.Generic;
using Sim.Constellation.Branches;
using Sim.Professions;
using UnityEngine;

namespace Sim.Constellation {
    // ConstellationNodeData vit dans son propre fichier (cf. ConstellationNodeData.cs) —
    // Unity exige une classe ScriptableObject par fichier pour résoudre la référence
    // MonoScript ; sinon les .asset générés s'écrivent avec m_Script: {fileID: 0}.

    [CreateAssetMenu(fileName = "ConstellationGraph", menuName = "Sim/Constellation/Graph Config")]
    public class ConstellationGraphConfig : ScriptableObject {
        public string centerNodeId = "center";
        public List<ConstellationNodeData> nodes = new List<ConstellationNodeData>();

        public ConstellationNodeData GetNode(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < nodes.Count; i++) {
                var n = nodes[i];
                if (n != null && n.id == id) return n;
            }
            return null;
        }

        // ── Résolution des refs de connectivité ────────────────────────────────
        // Itère les tableaux SO (connectedNodes / extraPrerequisites), en sautant les nulls.
        public IEnumerable<ConstellationNodeData> ResolveConnected(ConstellationNodeData node) {
            if (node == null || node.connectedNodes == null) yield break;
            for (int i = 0; i < node.connectedNodes.Length; i++) {
                var n = node.connectedNodes[i];
                if (n != null) yield return n;
            }
        }

        public IEnumerable<ConstellationNodeData> ResolveExtraPrereqs(ConstellationNodeData node) {
            if (node == null || node.extraPrerequisites == null) yield break;
            for (int i = 0; i < node.extraPrerequisites.Length; i++) {
                var n = node.extraPrerequisites[i];
                if (n != null) yield return n;
            }
        }

        // Lookup couleur/label d'une devise par son BranchConfig (source unique de vérité).
        public Color GetBranchColor(BranchConfig branch) => branch != null ? branch.color : Color.white;
        public string GetBranchLabel(BranchConfig branch) =>
            branch != null && !string.IsNullOrEmpty(branch.displayName) ? branch.displayName : (branch != null ? branch.id : "");

        // Graphe par défaut codé en dur : permet au prototype de tourner immédiatement
        // (touche K) sans qu'aucun asset n'ait été créé. Un asset
        // Resources/Configurations/Constellation/ConstellationGraph, s'il existe, le
        // remplace (voir MockConstellationDataProvider).
        //
        // C'est un ARBRE PUR : le centre relie 4 racines de branche, chaque branche est
        // un sous-arbre (parent → enfants). Aucun lien inter-branches : la disposition
        // orthogonale (ConstellationTreeLayout) garantit alors qu'aucun lien ne croise un
        // autre lien ni un nœud. Les nœuds hybrides restent des feuilles à parent unique
        // (leur double appartenance n'est plus qu'une info visuelle/teinte + panneau détail).
        public static ConstellationGraphConfig CreateDefault() {
            var cfg = CreateInstance<ConstellationGraphConfig>();
            cfg.centerNodeId = "center";
            var nodes = cfg.nodes;
            var conn = new Dictionary<string, string[]>();
            var prereq = new Dictionary<string, string[]>();

            BranchConfig B(string branchId) => BranchDatabase.ById(branchId);
            List<CostEntry> Cost(params (string id, int amt)[] entries) {
                var l = new List<CostEntry>();
                foreach (var e in entries) {
                    var b = B(e.id);
                    if (b != null && e.amt > 0) l.Add(new CostEntry { branch = b, amount = e.amt });
                }
                return l;
            }
            ConstellationNodeData Add(string id, string display, System.Action<ConstellationNodeData> init = null) {
                var nd = CreateInstance<ConstellationNodeData>();
                nd.id = id; nd.displayName = display; nd.name = id;
                init?.Invoke(nd);
                nodes.Add(nd);
                return nd;
            }

            // Nœud central (gratuit, toujours débloqué).
            Add("center", "Mon Broz", x => {
                x.isCenter = true;
                x.description = "Le cœur de votre constellation. Chaque action façonne votre identité.";
            });
            conn["center"] = new[] { "creatif_base", "ingenieux_base", "sportif_base", "sociable_base" };

            // ── Branche CRÉATIF ────────────────────────────────────────────
            Add("creatif_base", "Créatif", x => { x.branch = B("Creatif"); x.cost = Cost(("Creatif", 5)); x.description = "Le tronc de la branche Créatif."; });
            conn["creatif_base"] = new[] { "center", "creatif_cuisine", "creatif_musique", "creatif_dessin" };
            Add("creatif_cuisine", "Cuisine", x => { x.branch = B("Creatif"); x.cost = Cost(("Creatif", 10)); x.description = "Préparer des plats dans les cuisines."; x.activities = new[] { "Cuisine" }; });
            conn["creatif_cuisine"] = new[] { "creatif_base" };
            Add("creatif_musique", "Musique", x => { x.branch = B("Creatif"); x.cost = Cost(("Creatif", 14)); x.description = "Jouer d'un instrument dans les appartements et espaces publics."; x.activities = new[] { "Musique" }; });
            conn["creatif_musique"] = new[] { "creatif_base", "hybrid_artiste", "hybrid_organisateur" };
            Add("creatif_dessin", "Dessin", x => { x.branch = B("Creatif"); x.cost = Cost(("Creatif", 18)); x.description = "Réaliser des œuvres et croquis."; x.activities = new[] { "Dessin" }; });
            conn["creatif_dessin"] = new[] { "creatif_base", "creatif_deco" };
            Add("creatif_deco", "Décoration", x => { x.branch = B("Creatif"); x.cost = Cost(("Creatif", 28)); x.description = "Aménager et embellir les intérieurs."; x.activities = new[] { "Décoration" }; });
            conn["creatif_deco"] = new[] { "creatif_dessin" };

            // ── Branche MÉTIER (Ingénieux : arbre des métiers, racines gratuites) ──
            Add("ingenieux_base", "Métier", x => { x.branch = B("Ingenieux"); x.description = "L'arbre des métiers que tu pratiques en ville."; });
            conn["ingenieux_base"] = new[] { "center", "ingenieux_reparation", "ingenieux_construction", "ingenieux_logistique", "ingenious_delivery_driver" };
            Add("ingenieux_reparation", "Réparation", x => { x.branch = B("Ingenieux"); x.description = "Remettre en état des objets cassés."; x.activities = new[] { "Réparation" }; });
            conn["ingenieux_reparation"] = new[] { "ingenieux_base" };
            Add("ingenieux_construction", "Construction", x => { x.branch = B("Ingenieux"); x.description = "Bâtir et assembler des structures."; x.activities = new[] { "Construction" }; });
            conn["ingenieux_construction"] = new[] { "ingenieux_base" };
            Add("ingenieux_logistique", "Logistique", x => { x.branch = B("Ingenieux"); x.description = "Organiser et optimiser les flux de colis."; x.activities = new[] { "Logistique" }; });
            conn["ingenieux_logistique"] = new[] { "ingenieux_base" };

            // ── Sous-branche LIVREUR (devise delivery_driver, sous Ingénieux) ──
            Add("ingenious_delivery_driver", "Livreur", x => {
                x.branch = B("Ingenieux"); x.definesBranch = B("delivery_driver");
                x.description = "Métier de livreur. Donne accès aux missions du parcours livraison.";
                x.unlocks = new[] { "Métier de livreur", "Accès aux missions de livraison" };
            });
            conn["ingenious_delivery_driver"] = new[] { "ingenieux_base", "delivery_driver_sorting", "delivery_driver_packaging", "delivery_driver_delivery", "delivery_driver_speed", "delivery_driver_tips" };
            Add("delivery_driver_sorting", "Tri colis", x => { x.branch = B("Ingenieux"); x.cost = Cost(("delivery_driver", 4)); x.description = "Trier les colis à l'entrepôt selon leur destination."; x.unlocks = new[] { "Mission Tri colis" }; x.activities = new[] { "Trier à l'entrepôt" }; });
            conn["delivery_driver_sorting"] = new[] { "ingenious_delivery_driver" };
            Add("delivery_driver_packaging", "Emballage", x => { x.branch = B("Ingenieux"); x.cost = Cost(("delivery_driver", 5)); x.description = "Emballer les colis pour expédition."; x.unlocks = new[] { "Mission Emballage" }; x.activities = new[] { "Emballer les colis" }; });
            conn["delivery_driver_packaging"] = new[] { "ingenious_delivery_driver" };
            Add("delivery_driver_delivery", "Livraison", x => { x.branch = B("Ingenieux"); x.cost = Cost(("delivery_driver", 6)); x.description = "Livrer les colis directement aux clients."; x.unlocks = new[] { "Mission Livraison" }; x.activities = new[] { "Livrer aux clients" }; });
            conn["delivery_driver_delivery"] = new[] { "ingenious_delivery_driver", "delivery_driver_master" };
            Add("delivery_driver_speed", "Vitesse", x => { x.branch = B("Ingenieux"); x.cost = Cost(("delivery_driver", 5)); x.description = "Améliore ta vitesse de course pendant les livraisons."; x.unlocks = new[] { "+10 % vitesse de course (passif)" }; });
            conn["delivery_driver_speed"] = new[] { "ingenious_delivery_driver" };
            Add("delivery_driver_tips", "Pourboires", x => { x.branch = B("Ingenieux"); x.cost = Cost(("delivery_driver", 6)); x.description = "Les clients sont plus généreux à ton passage."; x.unlocks = new[] { "+15 % pourboires (passif)" }; });
            conn["delivery_driver_tips"] = new[] { "ingenious_delivery_driver", "delivery_driver_master" };
            Add("delivery_driver_master", "Maître Livreur", x => { x.branch = B("Ingenieux"); x.cost = Cost(("delivery_driver", 10)); x.description = "Apex du métier. Exige Livraison ET Pourboires débloqués."; x.unlocks = new[] { "Mission spéciale : livraison express", "+5 % gains tous métiers livreur" }; x.activities = new[] { "Coordonner les tournées" }; });
            conn["delivery_driver_master"] = new[] { "delivery_driver_delivery", "delivery_driver_tips" };
            prereq["delivery_driver_master"] = new[] { "delivery_driver_tips" };

            // ── Branche SPORTIF ────────────────────────────────────────────
            Add("sportif_base", "Sportif", x => { x.branch = B("Sportif"); x.cost = Cost(("Sportif", 5)); x.description = "Le tronc de la branche Sportif."; });
            conn["sportif_base"] = new[] { "center", "sportif_livraisons", "sportif_course", "sportif_velo" };
            Add("sportif_livraisons", "Livraisons", x => { x.branch = B("Sportif"); x.cost = Cost(("Sportif", 10)); x.description = "Acheminer des colis à travers la ville."; x.activities = new[] { "Livraisons" }; });
            conn["sportif_livraisons"] = new[] { "sportif_base", "hybrid_delivery_driver_expert" };
            Add("sportif_course", "Course", x => { x.branch = B("Sportif"); x.cost = Cost(("Sportif", 16)); x.description = "Se déplacer rapidement à pied."; x.activities = new[] { "Course" }; });
            conn["sportif_course"] = new[] { "sportif_base" };
            Add("sportif_velo", "Vélo", x => { x.branch = B("Sportif"); x.cost = Cost(("Sportif", 22)); x.description = "Parcourir de longues distances à vélo."; x.activities = new[] { "Vélo" }; });
            conn["sportif_velo"] = new[] { "sportif_base" };

            // ── Branche SOCIABLE ───────────────────────────────────────────
            Add("sociable_base", "Sociable", x => { x.branch = B("Sociable"); x.cost = Cost(("Sociable", 5)); x.description = "Le tronc de la branche Sociable."; });
            conn["sociable_base"] = new[] { "center", "sociable_rencontres", "sociable_groupes", "sociable_evenements" };
            Add("sociable_rencontres", "Rencontres", x => { x.branch = B("Sociable"); x.cost = Cost(("Sociable", 10)); x.description = "Faire connaissance avec d'autres Broz."; x.activities = new[] { "Rencontres" }; });
            conn["sociable_rencontres"] = new[] { "sociable_base" };
            Add("sociable_groupes", "Groupes", x => { x.branch = B("Sociable"); x.cost = Cost(("Sociable", 16)); x.description = "Fédérer et animer des groupes."; x.activities = new[] { "Groupes" }; });
            conn["sociable_groupes"] = new[] { "sociable_base", "hybrid_entrepreneur" };
            Add("sociable_evenements", "Événements", x => { x.branch = B("Sociable"); x.cost = Cost(("Sociable", 22)); x.description = "Mettre sur pied des événements."; x.activities = new[] { "Événements" }; });
            conn["sociable_evenements"] = new[] { "sociable_base", "sociable_reputation" };
            Add("sociable_reputation", "Réputation", x => { x.branch = B("Sociable"); x.cost = Cost(("Sociable", 32)); x.description = "Bâtir une réputation dans la ville."; x.activities = new[] { "Réputation" }; });
            conn["sociable_reputation"] = new[] { "sociable_evenements" };

            // ── Nœuds carrefour (ex-hybrides : coût multi-devises, feuilles à parent unique) ──
            Add("hybrid_artiste", "Artiste de rue", x => {
                x.branch = B("Creatif"); x.cost = Cost(("Creatif", 25), ("Sociable", 25));
                x.description = "À la croisée du Créatif et du Sociable : se produire en public.";
                x.unlocks = new[] { "Spectacles publics", "Pourboires", "Réputation artistique" };
                x.activities = new[] { "Donner un concert de rue", "Faire la manche en musique" };
            });
            conn["hybrid_artiste"] = new[] { "creatif_musique" };
            Add("hybrid_organisateur", "Organisateur Culturel", x => {
                x.branch = B("Creatif"); x.cost = Cost(("Creatif", 35), ("Sociable", 35));
                x.description = "À la croisée du Créatif et du Sociable : festivals et expositions.";
                x.unlocks = new[] { "Festivals", "Concerts", "Expositions" };
                x.activities = new[] { "Organiser un festival", "Monter une exposition" };
            });
            conn["hybrid_organisateur"] = new[] { "creatif_musique" };
            Add("hybrid_delivery_driver_expert", "Livreur Expert", x => {
                x.branch = B("Sportif"); x.cost = Cost(("Sportif", 25), ("Ingenieux", 25));
                x.description = "À la croisée du Sportif et de l'Ingénieux : les tournées avancées.";
                x.unlocks = new[] { "Tournées avancées", "Livraisons spéciales" };
                x.activities = new[] { "Accepter une tournée multi-colis", "Livrer un colis fragile" };
            });
            conn["hybrid_delivery_driver_expert"] = new[] { "sportif_livraisons" };
            Add("hybrid_entrepreneur", "Entrepreneur", x => {
                x.branch = B("Sociable"); x.cost = Cost(("Sociable", 25), ("Ingenieux", 25));
                x.description = "À la croisée du Sociable et de l'Ingénieux : gérer un commerce.";
                x.unlocks = new[] { "Gestion commerce", "Employés", "Contrats" };
                x.activities = new[] { "Ouvrir une boutique", "Embaucher un Broz" };
            });
            conn["hybrid_entrepreneur"] = new[] { "sociable_groupes" };

            // Résolution de la connectivité (IDs locaux → refs SO).
            var byId = new Dictionary<string, ConstellationNodeData>(nodes.Count);
            foreach (var nd in nodes) if (nd != null && !string.IsNullOrEmpty(nd.id)) byId[nd.id] = nd;
            ConstellationNodeData[] Resolve(string[] ids) {
                if (ids == null) return Array.Empty<ConstellationNodeData>();
                var list = new List<ConstellationNodeData>(ids.Length);
                foreach (var id in ids) if (byId.TryGetValue(id, out var t)) list.Add(t);
                return list.ToArray();
            }
            foreach (var nd in nodes) {
                if (conn.TryGetValue(nd.id, out var cids)) nd.connectedNodes = Resolve(cids);
                if (prereq.TryGetValue(nd.id, out var pids)) nd.extraPrerequisites = Resolve(pids);
            }

            return cfg;
        }
    }

    // Direction cardinale dans laquelle pousse une branche depuis le centre.
    public enum ConstellationDir { Up, Right, Down, Left }

    // Disposition « arbre soigné » orthogonale, calculée à l'exécution (le positionnement
    // procédural est autorisé). Chaque branche pousse dans sa direction cardinale ; au sein
    // d'une branche, les feuilles reçoivent des couloirs (lanes) distincts et chaque parent
    // est centré au-dessus de ses enfants. Comme les sous-arbres occupent des plages de
    // couloirs disjointes et que les liens sont des « équerres » coudées au mi-rang, aucun
    // lien ne peut croiser un autre lien ni un nœud.
    public static class ConstellationTreeLayout {
        public const float BaseDist = 400f;   // centre → racine de branche (élargi pour aérer les coins entre branches)
        public const float RankGap  = 280f;   // distance entre rangs successifs (cartes ~150 de haut)
        public const float LaneGap  = 280f;   // distance entre couloirs voisins (cartes ~130 de large) — élargi pour éviter qu'un nœud de Créatif touche un nœud d'Ingénieux au corner

        // Angle (degrés, repère trigo : 0 = +X droite, 90 = +Y haut) de la direction dans
        // laquelle pousse une branche RACINE depuis le centre. Modulaire : N branches racines
        // supportées. Les 4 canoniques gardent leurs cardinales historiques (par id) ; toute
        // branche racine supplémentaire occupe une diagonale libre, puis une distribution fine.
        // Une SOUS-branche hérite de l'angle de sa racine (TopLevelAncestor).
        private static readonly float[] ExtraAngles = { 45f, 135f, 225f, 315f, 22.5f, 67.5f, 112.5f, 157.5f, 202.5f, 247.5f, 292.5f, 337.5f };

        public static float RootAngleDeg(Branches.BranchConfig branch) {
            var root = Branches.BranchDatabase.TopLevelAncestor(branch);
            string id = root != null ? root.id : null;
            switch (id) {
                case "Creatif":   return 90f;   // haut
                case "Ingenieux": return 0f;    // droite
                case "Sportif":   return 270f;  // bas
                case "Sociable":  return 180f;  // gauche
            }
            // Branche racine non-canonique : index parmi les racines non-canoniques.
            int i = 0;
            foreach (var r in Branches.BranchDatabase.RootBranches()) {
                if (r == null) continue;
                string rid = r.id;
                if (rid == "Creatif" || rid == "Ingenieux" || rid == "Sportif" || rid == "Sociable") continue;
                if (r == root) break;
                i++;
            }
            return ExtraAngles[i % ExtraAngles.Length];
        }

        // True si l'axe primaire de la branche est plutôt vertical (bus de lien horizontal).
        public static bool IsVertical(float angleDeg) {
            float rad = angleDeg * Mathf.Deg2Rad;
            return Mathf.Abs(Mathf.Sin(rad)) >= Mathf.Abs(Mathf.Cos(rad));
        }

        // Écrit mapPosition sur chaque nœud. parentOut, si fourni, reçoit la carte
        // enfant→parent de l'arbre couvrant (utilisée par la vue pour ne tracer que les
        // arêtes parent→enfant).
        // Construit l'arbre couvrant (BFS depuis le centre) : parent enfant→parent,
        // profondeur, et enfants par nœud. Réutilisé par Apply (positionnement) ET par
        // ConstellationState (prérequis de déblocage : un nœud n'est dépensable que si son
        // parent est déjà débloqué).
        public static void BuildSpanningTree(ConstellationGraphConfig graph,
                                             out Dictionary<string, string> parent,
                                             out Dictionary<string, int> depth,
                                             out Dictionary<string, List<string>> children) {
            parent = new Dictionary<string, string>();
            depth = new Dictionary<string, int>();
            children = new Dictionary<string, List<string>>();
            if (graph == null || graph.nodes == null || graph.nodes.Count == 0) return;
            var center = graph.GetNode(graph.centerNodeId) ?? graph.nodes[0];
            if (center == null || string.IsNullOrEmpty(center.id)) return;

            // Adjacence non orientée. Garde-fou contre les entrées invalides (référence
            // d'asset cassée OU SO créé via clic-droit sans id renseigné) : on ignore
            // silencieusement ; le runtime reste stable et un warning unique pointe le
            // problème.
            var adj = new Dictionary<string, List<string>>();
            foreach (var node in graph.nodes) {
                if (node == null) { Debug.LogWarning("[Constellation] graph.nodes contains a null entry (broken asset reference)"); continue; }
                if (string.IsNullOrEmpty(node.id)) { Debug.LogWarning($"[Constellation] node asset has empty id: {node.name}"); continue; }
                if (!adj.ContainsKey(node.id)) adj[node.id] = new List<string>();
                foreach (var other in graph.ResolveConnected(node)) {
                    if (other == null || string.IsNullOrEmpty(other.id) || other.id == node.id) continue;
                    if (!adj.ContainsKey(other.id)) adj[other.id] = new List<string>();
                    if (!adj[node.id].Contains(other.id)) adj[node.id].Add(other.id);
                    if (!adj[other.id].Contains(node.id)) adj[other.id].Add(node.id);
                }
            }

            depth[center.id] = 0;
            var queue = new Queue<string>();
            queue.Enqueue(center.id);
            while (queue.Count > 0) {
                string cur = queue.Dequeue();
                foreach (var nb in adj[cur]) {
                    if (depth.ContainsKey(nb)) continue;
                    depth[nb] = depth[cur] + 1;
                    parent[nb] = cur;
                    if (!children.ContainsKey(cur)) children[cur] = new List<string>();
                    children[cur].Add(nb);
                    queue.Enqueue(nb);
                }
            }
        }

        public static void Apply(ConstellationGraphConfig graph, Dictionary<string, string> parentOut = null) {
            if (graph == null || graph.nodes == null || graph.nodes.Count == 0) return;
            var center = graph.GetNode(graph.centerNodeId) ?? graph.nodes[0];
            if (center == null) { Debug.LogWarning("[Constellation] Apply: center node is null, aborting layout"); return; }

            BuildSpanningTree(graph, out var parent, out var depth, out var children);
            if (parentOut != null) { parentOut.Clear(); foreach (var kv in parent) parentOut[kv.Key] = kv.Value; }

            center.mapPosition = Vector2.zero;

            // Chaque racine de branche = enfant direct du centre.
            if (!children.TryGetValue(center.id, out var bases)) return;
            foreach (var baseId in bases) {
                var baseNode = graph.GetNode(baseId);
                if (baseNode == null) continue;
                float ang = RootAngleDeg(baseNode.branch);

                // Attribution des couloirs (post-ordre) sur le sous-arbre de la branche.
                var lane = new Dictionary<string, float>();
                int leaf = 0;
                AssignLane(baseId, children, lane, ref leaf);

                float baseLane = lane[baseId];

                // Positionne tous les nœuds du sous-arbre.
                var stack = new Stack<string>();
                stack.Push(baseId);
                while (stack.Count > 0) {
                    string id = stack.Pop();
                    var node = graph.GetNode(id);
                    if (node != null) {
                        int rank = depth[id] - 1;            // racine de branche = rang 0
                        float l = lane[id] - baseLane;       // recentre la racine sur l'axe
                        node.mapPosition = DirPos(ang, BaseDist + rank * RankGap, l * LaneGap);
                    }
                    if (children.TryGetValue(id, out var kids))
                        foreach (var k in kids) stack.Push(k);
                }
            }
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

        // Convertit (distance radiale le long de l'axe de la branche, offset de couloir
        // perpendiculaire) en position, pour un angle quelconque.
        private static Vector2 DirPos(float angleDeg, float along, float laneOffset) {
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 fwd  = new Vector2(cos, sin);   // direction radiale de la branche
            Vector2 side = new Vector2(-sin, cos);  // perpendiculaire (couloir)
            return fwd * along + side * laneOffset;
        }
    }
}
