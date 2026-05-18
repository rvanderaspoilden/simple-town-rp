using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Tray contenant les vues d'items pas encore placés. Permet de retrouver
    /// l'item view sous le curseur pour démarrer un drag.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PackageItemTrayView : MonoBehaviour {
        [SerializeField] private RectTransform trayContainer;
        [SerializeField] private PackageItemView itemViewPrefab;
        [SerializeField] private float trayCellSize = 72f;

        private readonly Dictionary<int, PackageItemView> _views = new Dictionary<int, PackageItemView>();

        public IReadOnlyDictionary<int, PackageItemView> Views => _views;
        public float TrayCellSize => trayCellSize;

        public void Build(IReadOnlyList<PackageItemInstance> items) {
            foreach (var v in _views.Values) if (v != null) Destroy(v.gameObject);
            _views.Clear();

            for (int i = 0; i < items.Count; i++) {
                var inst = items[i];
                var view = Instantiate(itemViewPrefab, trayContainer);
                view.Bind(inst);
                view.SetTrayLayout(trayCellSize);
                _views[inst.Id] = view;
            }
        }

        /// <summary>
        /// Renvoie l'item dont la view se trouve sous le curseur, ou null.
        /// </summary>
        public PackageItemView PickAt(Vector2 screenPos, Camera uiCamera) {
            foreach (var kvp in _views) {
                var view = kvp.Value;
                if (view == null) continue;
                if (view.Instance.IsPlaced) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(view.Rect, screenPos, uiCamera)) {
                    return view;
                }
            }
            return null;
        }

        /// <summary>
        /// Replace l'item dans le tray (utilisé après un drag depuis le tray
        /// qui a échoué, ou un retrait depuis la grille). Réapplique le layout
        /// 1×1 du tray.
        /// </summary>
        public void RestoreView(int itemId) {
            if (_views.TryGetValue(itemId, out var view) && view != null) {
                view.transform.SetParent(trayContainer, false);
                view.gameObject.SetActive(true);
                view.SetTrayLayout(trayCellSize);
            }
        }
    }
}
