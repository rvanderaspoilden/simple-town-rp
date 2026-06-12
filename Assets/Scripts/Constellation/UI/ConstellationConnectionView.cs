using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Constellation {
    // Lien orthogonal parent → enfant. Deux rendus exclusifs selon l'état :
    // - non débloqué (au moins une extrémité verrouillée) → pointillés discrets ;
    // - débloqué (les deux extrémités débloquées) → trait plein lumineux.
    // Le tracé est toujours l'équerre à trois segments alignés sur les axes.
    public class ConstellationConnectionView : MonoBehaviour {
        [Header("Réglages")]
        [SerializeField] private Sprite dotSprite;
        [SerializeField] private Sprite solidSprite;
        [SerializeField] private float dotSize = 5f;
        [SerializeField] private float dotSpacing = 14f;
        [SerializeField] private float nodeInset = 70f;          // marge horizontale (cards ~134 de large)
        [SerializeField] private float nodeInsetVertical = 35f;  // marge verticale (cards plus courtes)
        [SerializeField] private float solidThickness = 3f;
        [SerializeField] private Color litColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] private Color dimColor = new Color(1f, 1f, 1f, 0.30f);

        private RectTransform _rect;
        private readonly List<Image> _dots = new List<Image>();
        private readonly List<Image> _solidSegs = new List<Image>();
        private bool _lit;

        public void FitElbow(Vector2 from, Vector2 to, bool verticalPrimary) {
            if (_rect == null) _rect = (RectTransform)transform;
            _rect.anchoredPosition = Vector2.zero;
            _rect.sizeDelta = Vector2.zero;
            ClearAll();

            Vector2 c1, c2;
            if (verticalPrimary) {
                float busY = (from.y + to.y) * 0.5f;
                c1 = new Vector2(from.x, busY);
                c2 = new Vector2(to.x, busY);
            } else {
                float busX = (from.x + to.x) * 0.5f;
                c1 = new Vector2(busX, from.y);
                c2 = new Vector2(busX, to.y);
            }

            // L'inset s'applique sur les stubs aux extrémités. Si le tracé est vertical
            // (branche Sportif/Créatif), les stubs sont verticaux → marge plus faible car
            // les cartes sont moins hautes que larges. Si horizontal (Ingénieux/Sociable),
            // on garde la marge horizontale plus grande.
            float startInset = verticalPrimary ? nodeInsetVertical : nodeInset;
            float endInset = verticalPrimary ? nodeInsetVertical : nodeInset;
            Vector2 segStart = InsetPoint(from, c1, startInset);
            Vector2 segEnd   = InsetPoint(to,   c2, endInset);

            // Pointillés
            PlaceDots(segStart, c1, includeEnd: false);
            PlaceDots(c1, c2,    includeEnd: false);
            PlaceDots(c2, segEnd, includeEnd: true);

            // Trait plein (équerre 3 segments solides)
            PlaceSolid(segStart, c1);
            PlaceSolid(c1, c2);
            PlaceSolid(c2, segEnd);

            ApplyState();
        }

        private static Vector2 InsetPoint(Vector2 from, Vector2 towards, float inset) {
            var delta = towards - from;
            float len = delta.magnitude;
            if (len <= inset) return towards;
            return from + delta * (inset / len);
        }

        private void PlaceDots(Vector2 a, Vector2 b, bool includeEnd) {
            float dist = Vector2.Distance(a, b);
            if (dist < dotSpacing * 0.5f) { if (includeEnd) SpawnDot(b); return; }

            // Snap les positions de points à une grille globale (multiples de dotSpacing).
            // Deux connexions qui partagent le même bus (frères enfants d'un même parent)
            // placent ainsi leurs points aux mêmes positions absolues → recouvrement
            // pixel-perfect au lieu d'un décalage qui produirait deux colonnes parallèles.
            bool horizontal = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);
            if (horizontal) {
                float y = (a.y + b.y) * 0.5f;
                float x0 = Mathf.Min(a.x, b.x);
                float x1 = Mathf.Max(a.x, b.x);
                float firstX = Mathf.Ceil(x0 / dotSpacing) * dotSpacing;
                for (float x = firstX; x <= x1 + 0.5f; x += dotSpacing) SpawnDot(new Vector2(x, y));
            } else {
                float x = (a.x + b.x) * 0.5f;
                float y0 = Mathf.Min(a.y, b.y);
                float y1 = Mathf.Max(a.y, b.y);
                float firstY = Mathf.Ceil(y0 / dotSpacing) * dotSpacing;
                for (float y = firstY; y <= y1 + 0.5f; y += dotSpacing) SpawnDot(new Vector2(x, y));
            }
            if (includeEnd) SpawnDot(b);
        }

        private void SpawnDot(Vector2 pos) {
            var go = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_rect, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(dotSize, dotSize);
            var img = go.GetComponent<Image>();
            if (dotSprite != null) img.sprite = dotSprite;
            img.raycastTarget = false;
            _dots.Add(img);
        }

        private void PlaceSolid(Vector2 a, Vector2 b) {
            float dist = Vector2.Distance(a, b);
            if (dist < 0.5f) return;
            bool horizontal = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);
            Vector2 mid = (a + b) * 0.5f;
            Vector2 size = horizontal
                ? new Vector2(Mathf.Abs(b.x - a.x) + solidThickness, solidThickness)
                : new Vector2(solidThickness, Mathf.Abs(b.y - a.y) + solidThickness);
            var go = new GameObject("Seg", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_rect, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = mid;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            if (solidSprite != null) { img.sprite = solidSprite; img.type = Image.Type.Sliced; }
            img.raycastTarget = false;
            _solidSegs.Add(img);
        }

        private void ClearAll() {
            for (int i = _dots.Count - 1; i >= 0; i--) if (_dots[i] != null) Destroy(_dots[i].gameObject);
            _dots.Clear();
            for (int i = _solidSegs.Count - 1; i >= 0; i--) if (_solidSegs[i] != null) Destroy(_solidSegs[i].gameObject);
            _solidSegs.Clear();
        }

        public void SetLit(bool lit) {
            _lit = lit;
            // Garantit l'ordre de rendu : les liens solides passent au-dessus des
            // pointillés. Quand plusieurs connexions partagent un même bus (frères d'un
            // même parent), la couche solide doit recouvrir les éventuels points placés
            // par les connexions verrouillées voisines.
            if (_rect != null) {
                if (lit) _rect.SetAsLastSibling();
                else _rect.SetAsFirstSibling();
            }
            ApplyState();
        }

        // Couleur du trait plein (= couleur de branche de l'enfant). Appelé par le MapView
        // au Build et après chaque rafraîchissement d'état.
        public void SetLitColor(Color color) { litColor = color; if (_lit) ApplyState(); }

        private void ApplyState() {
            for (int i = 0; i < _dots.Count; i++) {
                if (_dots[i] == null) continue;
                _dots[i].gameObject.SetActive(!_lit);
                _dots[i].color = dimColor;
            }
            for (int i = 0; i < _solidSegs.Count; i++) {
                if (_solidSegs[i] == null) continue;
                _solidSegs[i].gameObject.SetActive(_lit);
                _solidSegs[i].color = litColor;
            }
        }

        // Brève impulsion lumineuse jouée au déblocage (sur le trait plein).
        public void PlayTravel() {
            for (int i = 0; i < _solidSegs.Count; i++) {
                if (_solidSegs[i] == null) continue;
                var img = _solidSegs[i];
                img.DOKill();
                img.color = Color.white;
                img.DOColor(litColor, 0.45f).SetEase(Ease.OutCubic);
            }
        }
    }
}
