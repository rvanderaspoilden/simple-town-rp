using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// Tooltip suivant le curseur pour un slot d'inventaire/conteneur : icône + nom +
    /// description. Auto-construite (Screen Space Overlay, même approche que
    /// <see cref="HoverNameTooltip"/>) — aucun câblage de scène. Pilotée par
    /// DraggableItem au survol via l'API statique Show/Hide.
    /// </summary>
    public class ItemTooltipUI : MonoBehaviour {
        private static ItemTooltipUI _instance;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _panel;
        private CanvasGroup _group;
        private Image _icon;
        private TextMeshProUGUI _label;
        private TextMeshProUGUI _description;
        private TextMeshProUGUI _effects;
        private TextMeshProUGUI _container;

        private static readonly Vector2 CursorOffset = new Vector2(18f, -18f);

        public static void Show(Sprite icon, string label, string description,
            string effects = null, string container = null) {
            if (string.IsNullOrEmpty(label) && icon == null) { Hide(); return; }
            var t = Instance;

            bool hasIcon = icon != null;
            t._icon.gameObject.SetActive(hasIcon);
            if (hasIcon) t._icon.sprite = icon;

            t._label.text = label ?? string.Empty;

            bool hasDesc = !string.IsNullOrEmpty(description);
            t._description.gameObject.SetActive(hasDesc);
            if (hasDesc) t._description.text = description;

            bool hasFx = !string.IsNullOrEmpty(effects);
            t._effects.gameObject.SetActive(hasFx);
            if (hasFx) t._effects.text = effects;

            bool hasCont = !string.IsNullOrEmpty(container);
            t._container.gameObject.SetActive(hasCont);
            if (hasCont) t._container.text = container;

            LayoutRebuilder.ForceRebuildLayoutImmediate(t._panel);
            t._group.alpha = 1f;
            t.Reposition();
        }

        public static void Hide() {
            if (_instance != null) _instance._group.alpha = 0f;
        }

        // ── Formatage réutilisable des infos conteneur ────────────────────────
        /// <summary>Met en forme les infos conteneur (types acceptés en liste à puce + capacité).
        /// <paramref name="free"/> &lt; 0 = occupation inconnue → affiche la capacité.</summary>
        public static string FormatContainer(Sim.Scriptables.ContainerConfig cc, int free) {
            if (cc == null || !cc.IsContainer) return null;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>Conteneur</b>");
            if (cc.AcceptedTypes == null || cc.AcceptedTypes.Count == 0) sb.AppendLine("• tous objets");
            else foreach (var ty in cc.AcceptedTypes) sb.AppendLine("• " + TypeName(ty));
            if (cc.AcceptsProps) sb.AppendLine("• meubles");
            int slot = cc.SlotCount;
            if (free >= 0) sb.Append($"Espace : {free}/{slot} libre{(free > 1 ? "s" : "")}");
            else           sb.Append($"Capacité : {slot} emplacement{(slot > 1 ? "s" : "")}");
            return sb.ToString();
        }

        private static string TypeName(ItemType t) {
            switch (t) {
                case ItemType.CONSUMABLE: return "consommables";
                case ItemType.PACKAGE:    return "colis";
                case ItemType.WASTE:      return "déchets";
                default:                  return t.ToString().ToLower();
            }
        }

        private static ItemTooltipUI Instance {
            get {
                if (_instance == null) _instance = Build();
                return _instance;
            }
        }

        private static ItemTooltipUI Build() {
            GameObject root = new GameObject("ItemTooltipUI");
            DontDestroyOnLoad(root);

            ItemTooltipUI t = root.AddComponent<ItemTooltipUI>();
            t._canvas = root.AddComponent<Canvas>();
            t._canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            t._canvas.sortingOrder = 1200; // au-dessus du menu contextuel (1000)
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            t._canvasRect = root.GetComponent<RectTransform>();

            // Panneau : fond + layout vertical + auto-size.
            GameObject panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(root.transform, false);
            t._panel = panelGo.AddComponent<RectTransform>();
            t._panel.anchorMin = t._panel.anchorMax = new Vector2(0.5f, 0.5f);
            t._panel.pivot = new Vector2(0f, 1f); // coin haut-gauche → s'ouvre vers le bas-droite du curseur

            Image bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.05f, 0.04f, 0.9f);
            bg.raycastTarget = false;

            t._group = panelGo.AddComponent<CanvasGroup>();
            t._group.alpha = 0f;
            t._group.interactable = false;
            t._group.blocksRaycasts = false;

            VerticalLayoutGroup vlg = panelGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;  vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;

            ContentSizeFitter fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // Icône.
            GameObject iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(panelGo.transform, false);
            t._icon = iconGo.AddComponent<Image>();
            t._icon.raycastTarget = false;
            t._icon.preserveAspect = true;
            LayoutElement iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 56f; iconLe.preferredHeight = 56f;

            // Nom (gras).
            t._label = MakeText(panelGo.transform, "Label", 18f, FontStyles.Bold, Color.white, 240f);
            // Description (plus petit, gris clair).
            t._description = MakeText(panelGo.transform, "Description", 13f, FontStyles.Normal,
                new Color(0.85f, 0.83f, 0.8f, 1f), 240f);
            // Effets (consommables) — rich text coloré, fourni déjà formaté par l'appelant.
            t._effects = MakeText(panelGo.transform, "Effects", 13f, FontStyles.Normal,
                Color.white, 240f);
            // Infos conteneur (type de stockage, capacité, espace restant).
            t._container = MakeText(panelGo.transform, "Container", 12.5f, FontStyles.Normal,
                new Color(0.7f, 0.86f, 1f, 1f), 240f);

            return t;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, float size,
            FontStyles style, Color color, float maxWidth) {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = maxWidth;
            return tmp;
        }

        private void LateUpdate() {
            if (_group != null && _group.alpha > 0f) Reposition();
        }

        private void Reposition() {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, Input.mousePosition, null, out Vector2 local);

            Vector2 pos = local + CursorOffset;

            Rect canvas = _canvasRect.rect;
            Vector2 size = _panel.sizeDelta;
            float halfW = canvas.width  * 0.5f;
            float halfH = canvas.height * 0.5f;
            // pivot haut-gauche : x s'étend à droite, y vers le bas.
            if (pos.x + size.x > halfW)  pos.x = local.x - CursorOffset.x - size.x;
            if (pos.y - size.y < -halfH) pos.y = local.y + size.y;

            _panel.anchoredPosition = pos;
        }
    }
}
