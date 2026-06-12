using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Sim.Constellation {
    // Tooltip flottant affiché au survol d'un nœud. Contenu : description + section
    // Prérequis (points requis + nœuds requis) + listes Déblocages / Activités. Pas de
    // titre (la carte le porte déjà), pas de bouton. Positionné à droite (ou à gauche en
    // cas de débordement) du nœud survolé, attaché à la racine de la modale pour ne pas
    // subir le pan/zoom de la carte.
    public class ConstellationDetailPanel : MonoBehaviour {
        [Header("Refs prefab")]
        [SerializeField] private GameObject root;
        [SerializeField] private RectTransform tooltipRect;     // racine positionnable du tooltip
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private GameObject prereqHeader;
        [SerializeField] private RectTransform prereqContainer;
        [SerializeField] private GameObject unlocksHeader;
        [SerializeField] private RectTransform unlocksContainer;
        [SerializeField] private GameObject activitiesHeader;
        [SerializeField] private RectTransform activitiesContainer;
        [SerializeField] private TextMeshProUGUI listEntryTemplate;

        [Header("Placement")]
        // Décalage depuis le coin haut-droit du nœud. Volontairement faible pour que
        // le tooltip soit visuellement « collé » à la carte — un offset trop grand
        // donne l'impression d'un panneau flottant indépendant.
        [SerializeField] private Vector2 hoverOffset = new Vector2(10f, 0f);
        [SerializeField] private float screenMargin = 12f;

        // Vue actuellement « suivie » par le tooltip. On la garde pour ré-ancrer
        // chaque LateUpdate : si le contenu de la carte zoome/pan pendant que le
        // tooltip est visible, le tooltip doit rester collé au nœud.
        private ConstellationNodeView _tracking;

        public void ShowFor(ConstellationNodeView nodeView, IConstellationDataProvider provider) {
            if (nodeView == null || nodeView.Node == null) return;
            Populate(nodeView.Node, provider);
            if (root != null) root.SetActive(true);
            _tracking = nodeView;
            PositionNear(nodeView);
        }

        public void Hide() {
            _tracking = null;
            if (root != null) root.SetActive(false);
        }

        // Suit le nœud même quand la carte zoome/pan ; sans ce LateUpdate, le tooltip
        // reste figé à la position du nœud au moment du HoverEnter et dérive visuellement.
        private void LateUpdate() {
            if (_tracking != null && root != null && root.activeSelf) {
                PositionNear(_tracking);
            }
        }

        private void Populate(ConstellationNodeData node, IConstellationDataProvider provider) {
            if (descriptionText != null) descriptionText.text = node.description;

            var state = provider != null ? provider.State : null;
            var prereqEntries = BuildPrereqEntries(node, provider, state);
            bool hasPrereqs = prereqEntries.Count > 0;
            if (prereqHeader != null) prereqHeader.SetActive(hasPrereqs);
            if (prereqContainer != null) prereqContainer.gameObject.SetActive(hasPrereqs);
            if (hasPrereqs) PopulateRichList(prereqContainer, prereqEntries);

            bool hasUnlocks = node.unlocks != null && node.unlocks.Length > 0;
            bool hasActivities = node.activities != null && node.activities.Length > 0;
            if (unlocksHeader != null) unlocksHeader.SetActive(hasUnlocks);
            if (unlocksContainer != null) unlocksContainer.gameObject.SetActive(hasUnlocks);
            if (activitiesHeader != null) activitiesHeader.SetActive(hasActivities);
            if (activitiesContainer != null) activitiesContainer.gameObject.SetActive(hasActivities);

            if (hasUnlocks) PopulateList(unlocksContainer, node.unlocks);
            if (hasActivities) PopulateList(activitiesContainer, node.activities);
        }

        // Construit la liste textuelle des prérequis : coûts (avec couleur de devise + un
        // indicateur OK/manque selon le solde courant) puis nœuds requis (parent d'arbre +
        // extras), chacun marqué selon son état de déblocage. Vide pour un nœud central ou
        // déjà débloqué.
        private List<string> BuildPrereqEntries(ConstellationNodeData node, IConstellationDataProvider provider, ConstellationState state) {
            var list = new List<string>();
            if (node == null || provider == null || state == null) return list;
            if (node.isCenter || state.IsUnlocked(node)) return list;

            var graph = provider.Graph;

            // Coûts par devise (liste agnostique) + indicateur affordable. Label + couleur
            // viennent du BranchConfig de chaque entrée.
            foreach (var e in state.CostsOf(node)) {
                bool ok = state.GetAvailable(e.branch.id) >= e.amount;
                list.Add(CostEntry(e.amount, graph.GetBranchLabel(e.branch), graph.GetBranchColor(e.branch), ok));
            }

            // Nœuds requis : parent d'arbre + extras. Déduplication au cas où.
            var prereqIds = new List<string>();
            var parentId = state.GetParentId(node.id);
            if (!string.IsNullOrEmpty(parentId) && !prereqIds.Contains(parentId)) prereqIds.Add(parentId);
            foreach (var extra in graph.ResolveExtraPrereqs(node))
                if (extra != null && !string.IsNullOrEmpty(extra.id) && !prereqIds.Contains(extra.id)) prereqIds.Add(extra.id);
            foreach (var id in prereqIds) {
                var pr = graph.GetNode(id);
                if (pr == null) continue;
                // Le centre est toujours « déjà débloqué » mais peu informatif → on l'omet.
                if (pr.isCenter) continue;
                bool done = state.IsUnlocked(pr);
                list.Add("Nœud : <b>" + pr.displayName + "</b>  " + Mark(done));
            }

            return list;
        }

        // « Marque » colorée affichée à droite de chaque entrée. On utilise des LETTRES
        // ASCII (v / x) car les glyphes ✓ / ✗ ne sont pas dans toutes les fontes TMP du
        // projet — ils tombent en boîtes vides quand absents.
        private static string Mark(bool ok) {
            return ok
                ? "<color=#7AC97A><b>v</b></color>"
                : "<color=#E07070><b>x</b></color>";
        }

        private static string CostEntry(int amount, string label, Color color, bool affordable) {
            string hex = ColorUtility.ToHtmlStringRGB(color);
            return "<color=#" + hex + "><b>" + amount + "</b> " + label + "</color>  " + Mark(affordable);
        }

        private void PositionNear(ConstellationNodeView nodeView) {
            if (tooltipRect == null) return;
            var nodeRT = (RectTransform)nodeView.transform;
            var parentRT = tooltipRect.parent as RectTransform;
            if (parentRT == null) return;

            // Force layout pour que la hauteur soit à jour avant placement.
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

            Vector3[] corners = new Vector3[4]; // 0=BL, 1=TL, 2=TR, 3=BR
            nodeRT.GetWorldCorners(corners);
            Vector3 nodeTopRight = corners[2];
            Vector3 nodeTopLeft  = corners[1];

            Vector2 tipSize = tooltipRect.rect.size * tooltipRect.lossyScale.x;
            Vector2 pivot   = tooltipRect.pivot;
            Vector3[] parentCorners = new Vector3[4];
            parentRT.GetWorldCorners(parentCorners);
            float parentLeft   = parentCorners[0].x + screenMargin;
            float parentRight  = parentCorners[2].x - screenMargin;
            float parentBottom = parentCorners[0].y + screenMargin;
            float parentTop    = parentCorners[1].y - screenMargin;

            // On veut que le BORD GAUCHE du tooltip soit collé au bord droit du
            // nœud (+ hoverOffset). On calcule la cible du bord-gauche puis on
            // décale du pivot pour obtenir la world-position à donner à .position.
            float leftEdgeTargetX = nodeTopRight.x + hoverOffset.x;
            float topEdgeTargetY  = nodeTopRight.y + hoverOffset.y;
            // Fallback à gauche du nœud si le tooltip déborderait à droite.
            if (leftEdgeTargetX + tipSize.x > parentRight) {
                leftEdgeTargetX = nodeTopLeft.x - hoverOffset.x - tipSize.x;
            }
            leftEdgeTargetX = Mathf.Clamp(leftEdgeTargetX, parentLeft, parentRight - tipSize.x);
            // Le tooltip s'étend vers le bas depuis le top → on clamp pour qu'il
            // ne descende pas hors écran.
            float topMin = parentBottom + tipSize.y;
            topEdgeTargetY = Mathf.Clamp(topEdgeTargetY, topMin, parentTop);

            Vector3 desired = new Vector3(
                leftEdgeTargetX + tipSize.x * pivot.x,
                topEdgeTargetY - tipSize.y * (1f - pivot.y),
                0f
            );
            tooltipRect.position = desired;
        }

        private void PopulateList(RectTransform container, string[] entries) {
            if (container == null || listEntryTemplate == null) return;
            ClearList(container);
            if (entries == null) return;
            foreach (var entry in entries) {
                var row = Instantiate(listEntryTemplate, container);
                row.gameObject.SetActive(true);
                row.text = "• " + entry;
            }
        }

        private void PopulateRichList(RectTransform container, List<string> richEntries) {
            if (container == null || listEntryTemplate == null) return;
            ClearList(container);
            foreach (var entry in richEntries) {
                var row = Instantiate(listEntryTemplate, container);
                row.gameObject.SetActive(true);
                row.richText = true;
                row.text = "• " + entry;
            }
        }

        private void ClearList(RectTransform container) {
            for (int i = container.childCount - 1; i >= 0; i--) {
                var child = container.GetChild(i);
                if (child == listEntryTemplate.transform) continue;
                Destroy(child.gameObject);
            }
        }
    }
}
