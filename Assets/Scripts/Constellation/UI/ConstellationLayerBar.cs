using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Constellation {
    // Barre de navigation des couches. L'UI est AUTORÉE dans le prefab « Constellation UI »
    // (conteneur + onglet template désactivé, à la manière de ConstellationProfileHeader et
    // son branchCounterTemplate). Ce composant ne fait que cloner le template pour chaque
    // couche de premier niveau (+ « Vue globale ») et, en sous-couche, ajouter le fil
    // d'Ariane. Cliquer un onglet affiche la couche correspondante.
    public class ConstellationLayerBar : MonoBehaviour {
        [Header("Refs prefab")]
        [Tooltip("Conteneur des onglets (porte idéalement un HorizontalLayoutGroup).")]
        [SerializeField] private RectTransform tabsContainer;
        [Tooltip("Onglet template (désactivé) : Image (fond) + Button + enfant « Label » (TMP).")]
        [SerializeField] private GameObject tabTemplate;

        [Header("Couleurs onglet inactif")]
        [SerializeField] private Color inactiveBg = new Color(0.16f, 0.15f, 0.19f, 0.92f);
        [SerializeField] private Color inactiveText = new Color(0.78f, 0.78f, 0.84f);

        private ConstellationMapView _mapView;
        private readonly Dictionary<string, ConstellationLayer> _byKey = new Dictionary<string, ConstellationLayer>();

        public void Initialize(ConstellationMapView mapView) {
            _mapView = mapView;
            _byKey.Clear();
            foreach (var l in mapView.Layers) _byKey[l.key] = l;

            if (tabTemplate != null) tabTemplate.SetActive(false);

            _mapView.LayerChanged -= OnLayerChanged;
            _mapView.LayerChanged += OnLayerChanged;
            OnLayerChanged(_mapView.CurrentLayer);
        }

        private void OnDestroy() {
            if (_mapView != null) _mapView.LayerChanged -= OnLayerChanged;
        }

        private void OnLayerChanged(ConstellationLayer current) {
            if (tabsContainer == null || tabTemplate == null || current == null) return;

            // Détruit les clones (tout sauf le template).
            for (int i = tabsContainer.childCount - 1; i >= 0; i--) {
                var child = tabsContainer.GetChild(i);
                if (child.gameObject == tabTemplate) continue;
                Destroy(child.gameObject);
            }

            // Chaîne des couches imbriquées sous le premier niveau (haut → courant).
            var chain = new List<ConstellationLayer>();
            var c = current;
            int guard = 0;
            while (c != null && !c.isTopLevel && guard++ < 64) {
                chain.Insert(0, c);
                c = _byKey.TryGetValue(c.parentLayerKey, out var p) ? p : null;
            }
            string topAncestorKey = c != null ? c.key : current.key;

            // Onglets de premier niveau (une branche racine chacun).
            foreach (var l in _mapView.Layers)
                if (l.isTopLevel) AddTab(l, l.key == topAncestorKey);

            // Fil d'Ariane pour les sous-couches.
            foreach (var sub in chain) AddTab(sub, sub.key == current.key);
        }

        private void AddTab(ConstellationLayer layer, bool active) {
            var go = Instantiate(tabTemplate, tabsContainer);
            go.SetActive(true);
            go.name = "Tab_" + layer.key;

            var bg = go.GetComponent<Image>();
            if (bg != null) bg.color = active
                ? new Color(layer.color.r, layer.color.g, layer.color.b, 0.92f)
                : inactiveBg;

            var labelTr = go.transform.Find("Label");
            var label = labelTr != null ? labelTr.GetComponent<TextMeshProUGUI>()
                                        : go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) {
                label.text = layer.displayName;
                label.color = active ? ContrastOn(layer.color) : inactiveText;
            }

            var btn = go.GetComponent<Button>();
            if (btn != null) {
                string key = layer.key;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => _mapView.ShowLayer(key));
            }
        }

        // Texte sombre sur fond clair, clair sur fond sombre (luminance perçue).
        private static Color ContrastOn(Color bg) {
            float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.6f ? new Color(0.1f, 0.1f, 0.12f) : Color.white;
        }
    }
}
