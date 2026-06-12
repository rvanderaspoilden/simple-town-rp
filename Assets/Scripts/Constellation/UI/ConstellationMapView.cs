using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim.Constellation {
    // Carte navigable, rendue UNE COUCHE À LA FOIS. Le graphe est découpé par
    // ConstellationLayerModel en couches indépendantes : la couche globale (centrée sur
    // « Mon Broz », montrant la base de chaque branche) et une couche par (sous-)branche
    // (centrée sur son nœud base, ne montrant que ses nœuds). Cliquer un nœud base/portail
    // descend dans la couche associée ; cliquer la base de la couche courante remonte.
    //
    // Porté par le viewport (Image raycast-target + RectMask2D). Les nœuds et les lignes
    // sont instanciés depuis leurs prefabs ; seuls l'instanciation, le positionnement et
    // la mise en place des lignes sont procéduraux.
    public class ConstellationMapView : MonoBehaviour, IDragHandler, IScrollHandler, IPointerClickHandler {
        [Header("Refs")]
        [SerializeField] private RectTransform content;                 // conteneur des nœuds (pivot centre)
        [SerializeField] private ConstellationNodeView nodePrefab;
        [SerializeField] private ConstellationConnectionView connectionPrefab;

        [Header("Zoom")]
        [SerializeField] private float minZoom = 0.4f;
        [SerializeField] private float maxZoom = 2.2f;
        [SerializeField] private float zoomStep = 0.12f;
        [SerializeField] private float defaultZoom = 1f;

        public event Action<ConstellationNodeView> NodeHoverEnter;
        public event Action<ConstellationNodeView> NodeHoverExit;
        public event Action<ConstellationNodeData> NodeUnlockRequested;
        public event Action BackgroundClicked;
        // Émis après chaque changement de couche affichée (pour synchroniser la barre d'onglets).
        public event Action<ConstellationLayer> LayerChanged;

        private IConstellationDataProvider _provider;
        private readonly Dictionary<string, ConstellationNodeView> _nodeViews = new Dictionary<string, ConstellationNodeView>();
        private readonly List<ConnectionLink> _links = new List<ConnectionLink>();

        private List<ConstellationLayer> _layers = new List<ConstellationLayer>();
        private readonly Dictionary<string, ConstellationLayer> _layersByKey = new Dictionary<string, ConstellationLayer>();
        private ConstellationLayer _currentLayer;

        // Lock global posé pendant une anim de déblocage : empêche tous les boutons
        // Débloquer de répondre tant que l'absorption n'est pas terminée.
        private bool _unlockLock;

        // Dernier nœud survolé (HoverEnter/Exit). Utilisé par OnScroll pour ancrer le zoom.
        private ConstellationNodeView _hoveredView;

        private float _lastClickTime = -1f;

        private struct ConnectionLink {
            public ConstellationConnectionView view;
            public ConstellationNodeData a;
            public ConstellationNodeData b;
        }

        public IReadOnlyList<ConstellationLayer> Layers => _layers;
        public ConstellationLayer CurrentLayer => _currentLayer;
        public ConstellationLayer GetLayer(string key) =>
            !string.IsNullOrEmpty(key) && _layersByKey.TryGetValue(key, out var l) ? l : null;

        public void Build(IConstellationDataProvider provider) {
            _provider = provider;

            _layers = ConstellationLayerModel.Build(provider.Graph);
            _layersByKey.Clear();
            foreach (var l in _layers) _layersByKey[l.key] = l;

            ShowDefault(true);
        }

        // Affiche la couche par défaut : la première couche de premier niveau (la « vue
        // globale »/centre a été retirée — les onglets couvrent la navigation).
        public void ShowDefault(bool instant = false) {
            ConstellationLayer target = null;
            foreach (var l in _layers) if (l.isTopLevel) { target = l; break; }
            if (target == null && _layers.Count > 0) target = _layers[0];
            if (target != null) ShowLayer(target.key, instant);
        }

        // Rebuild complet de la carte pour n'afficher que les nœuds/liens de la couche `key`.
        public void ShowLayer(string key, bool instant = false) {
            if (_provider == null || string.IsNullOrEmpty(key)) return;
            if (!_layersByKey.TryGetValue(key, out var layer)) return;

            _currentLayer = layer;
            Clear();

            var graph = _provider.Graph;
            var members = new HashSet<string>(layer.memberNodeIds);

            // Layout radial : base au centre, enfants autour.
            var layout = ConstellationLayerLayout.Apply(graph, layer);

            // LinkLayer : conteneur dédié aux liens, premier sibling de content (rendu sous
            // les nœuds). Le SetLit qui réordonne les liens reste confiné dans ce conteneur.
            var linkLayerGo = new GameObject("LinkLayer", typeof(RectTransform));
            var linkLayer = (RectTransform)linkLayerGo.transform;
            linkLayer.SetParent(content, false);
            linkLayer.anchorMin = Vector2.zero;
            linkLayer.anchorMax = Vector2.one;
            linkLayer.offsetMin = Vector2.zero;
            linkLayer.offsetMax = Vector2.zero;
            linkLayer.SetAsFirstSibling();

            // 1) Liens parent→enfant (arbre de la couche).
            foreach (var kv in layout.parentMap) {
                var child = graph.GetNode(kv.Key);
                var par = graph.GetNode(kv.Value);
                if (child == null || par == null) continue;
                AddLink(linkLayer, layout, par, child, graph.GetBranchColor(child.branch));
            }

            // 1bis) Prérequis additionnels dont les deux extrémités sont dans la couche.
            foreach (var id in layer.memberNodeIds) {
                var node = graph.GetNode(id);
                if (node == null) continue;
                foreach (var prereq in graph.ResolveExtraPrereqs(node)) {
                    if (prereq == null || !members.Contains(prereq.id)) continue;
                    AddLink(linkLayer, layout, prereq, node, graph.GetBranchColor(node.branch));
                }
            }

            // 2) Nœuds.
            foreach (var id in layer.memberNodeIds) {
                var node = graph.GetNode(id);
                if (node == null) continue;
                var view = Instantiate(nodePrefab, content);
                ((RectTransform)view.transform).anchoredPosition =
                    layout.positions.TryGetValue(id, out var p) ? p : Vector2.zero;
                view.UnlockRequested += OnUnlockRequested;
                view.HoverEnter += v => { _hoveredView = v; NodeHoverEnter?.Invoke(v); };
                view.HoverExit  += v => { if (_hoveredView == v) _hoveredView = null; NodeHoverExit?.Invoke(v); };
                view.Clicked += OnNodeClicked;
                _nodeViews[id] = view;
            }

            RefreshStates();

            // Replace chaque carte à sa position après que VLG+CSF aient calculé sa hauteur,
            // pour rester centrée quand la carte grandit/rétrécit au déblocage.
            foreach (var kv in _nodeViews) {
                var rt = (RectTransform)kv.Value.transform;
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                rt.anchoredPosition = layout.positions.TryGetValue(kv.Key, out var p) ? p : Vector2.zero;
            }

            ResetView(true);
            LayerChanged?.Invoke(_currentLayer);
        }

        private void AddLink(RectTransform linkLayer, ConstellationLayerLayout.Result layout,
                             ConstellationNodeData par, ConstellationNodeData child, Color color) {
            Vector2 pa = layout.positions.TryGetValue(par.id, out var a) ? a : Vector2.zero;
            Vector2 pb = layout.positions.TryGetValue(child.id, out var b) ? b : Vector2.zero;
            // Axe primaire déduit de la géométrie réelle du lien (radial) : vertical si
            // l'écart Y domine. Plus robuste que l'angle de branche en disposition radiale.
            bool vertical = Mathf.Abs(pb.y - pa.y) >= Mathf.Abs(pb.x - pa.x);
            var view = Instantiate(connectionPrefab, linkLayer);
            view.FitElbow(pa, pb, vertical);
            view.SetLitColor(color);
            _links.Add(new ConnectionLink { view = view, a = par, b = child });
        }

        private void OnNodeClicked(ConstellationNodeView v) {
            if (v?.Node == null || _currentLayer == null) return;
            string id = v.Node.id;

            // Portail (base d'une sous-couche présente dans la couche courante) → on descend.
            if (_currentLayer.portalNodeIds.Contains(id) && _layersByKey.ContainsKey(id)) {
                ShowLayer(id);
                return;
            }
            // Clic sur la base de la couche courante → on remonte vers la couche parente.
            if (id == _currentLayer.baseNodeId && !string.IsNullOrEmpty(_currentLayer.parentLayerKey)) {
                ShowLayer(_currentLayer.parentLayerKey);
                return;
            }
            // Nœud ordinaire → recentre la carte dessus.
            FocusNode(id);
        }

        public void RefreshStates() {
            if (_provider == null || _currentLayer == null) return;
            var graph = _provider.Graph;
            var state = _provider.State;

            foreach (var kv in _nodeViews) {
                var node = graph.GetNode(kv.Key);
                if (node == null) continue;
                Color col = graph.GetBranchColor(node.branch);
                string lbl = graph.GetBranchLabel(node.branch);
                kv.Value.Setup(node, state, col, lbl);
                if (_unlockLock) kv.Value.SetUnlockButtonInteractable(false);
            }

            foreach (var link in _links) {
                bool lit = state.IsUnlocked(link.a) && state.IsUnlocked(link.b);
                link.view.SetLit(lit);
            }
        }

        public void SetUnlockLock(bool locked) {
            _unlockLock = locked;
            foreach (var kv in _nodeViews) {
                if (locked) kv.Value.SetUnlockButtonInteractable(false);
            }
            if (!locked) RefreshStates();
        }

        public void PlayUnlock(ConstellationNodeData node) {
            if (_nodeViews.TryGetValue(node.id, out var view)) view.PlayUnlockPop();
            foreach (var link in _links) {
                if (link.a.id != node.id && link.b.id != node.id) continue;
                var otherUnlocked = _provider.State.IsUnlocked(link.a.id == node.id ? link.b : link.a);
                if (otherUnlocked) { link.view.SetLit(true); link.view.PlayTravel(); }
            }
        }

        public ConstellationNodeView GetNodeView(string nodeId) {
            return _nodeViews.TryGetValue(nodeId, out var v) ? v : null;
        }

        // Recentre sur un nœud, en basculant d'abord vers la couche qui le contient si
        // besoin (utilisé par la recherche, qui peut viser un nœud hors couche courante).
        public void RevealNode(string nodeId) {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (_nodeViews.ContainsKey(nodeId)) { FocusNode(nodeId); return; }
            foreach (var l in _layers) {
                if (l.memberNodeIds.Contains(nodeId)) { ShowLayer(l.key); FocusNode(nodeId); return; }
            }
        }

        public void FocusNode(string nodeId, bool instant = false) {
            if (!_nodeViews.ContainsKey(nodeId)) return;
            var rt = (RectTransform)_nodeViews[nodeId].transform;
            Vector2 target = -rt.anchoredPosition * content.localScale.x;
            content.DOComplete();
            if (instant) content.anchoredPosition = target;
            else content.DOAnchorPos(target, 0.4f).SetEase(Ease.OutCubic);
        }

        public void ResetView(bool instant = false) {
            content.DOComplete();
            if (instant) {
                content.anchoredPosition = Vector2.zero;
                content.localScale = Vector3.one * defaultZoom;
            } else {
                content.DOAnchorPos(Vector2.zero, 0.4f).SetEase(Ease.OutCubic);
                content.DOScale(Vector3.one * defaultZoom, 0.4f).SetEase(Ease.OutCubic);
            }
        }

        // ── Navigation ─────────────────────────────────────────────────────
        public void OnDrag(PointerEventData eventData) {
            content.anchoredPosition += eventData.delta;
        }

        public void OnScroll(PointerEventData eventData) {
            float oldZoom = content.localScale.x;
            float newZoom = Mathf.Clamp(oldZoom + eventData.scrollDelta.y * zoomStep, minZoom, maxZoom);
            if (Mathf.Approximately(newZoom, oldZoom)) return;

            var viewport = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera : null;

            Vector2 viewportAnchor;
            if (_hoveredView != null && _hoveredView.Node != null) {
                Vector2 mapPos = ((RectTransform)_hoveredView.transform).anchoredPosition;
                viewportAnchor = content.anchoredPosition + mapPos * oldZoom;
            } else {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    viewport, eventData.position, uiCam, out viewportAnchor);
            }

            Vector2 contentPoint = (viewportAnchor - content.anchoredPosition) / oldZoom;
            content.localScale = Vector3.one * newZoom;
            content.anchoredPosition = viewportAnchor - contentPoint * newZoom;
        }

        public void OnPointerClick(PointerEventData eventData) {
            // Double-clic sur le fond → recentre. Simple clic sur le fond → ferme le détail.
            float now = Time.unscaledTime;
            if (now - _lastClickTime < 0.3f) {
                ResetView();
                _lastClickTime = -1f;
            } else {
                _lastClickTime = now;
                BackgroundClicked?.Invoke();
            }
        }

        private void OnUnlockRequested(ConstellationNodeData node) {
            if (node == null) return;
            NodeUnlockRequested?.Invoke(node);
        }

        private void Clear() {
            foreach (var kv in _nodeViews) if (kv.Value != null) Destroy(kv.Value.gameObject);
            foreach (var link in _links) if (link.view != null) Destroy(link.view.gameObject);
            _nodeViews.Clear();
            _links.Clear();
            // Détruit tout LinkLayer présent (sera recréé au prochain ShowLayer).
            for (int i = content.childCount - 1; i >= 0; i--) {
                var c = content.GetChild(i);
                if (c.name == "LinkLayer") Destroy(c.gameObject);
            }
        }
    }
}
