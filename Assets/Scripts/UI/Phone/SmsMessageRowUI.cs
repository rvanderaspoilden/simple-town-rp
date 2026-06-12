using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// One chat bubble in the SMS conversation. Cloned from the Message Row template.
    /// The row is a full-width container whose HorizontalLayoutGroup aligns the bubble
    /// left (received) or right (sent). The bubble hugs its text up to a max width.
    /// The "Lu / Non lu" line is shown only on the local player's own (sent) messages.
    /// </summary>
    public class SmsMessageRowUI : MonoBehaviour {
        [SerializeField] private HorizontalLayoutGroup layout;
        [SerializeField] private Image bubble;
        [SerializeField] private LayoutElement bubbleLayout;
        [SerializeField] private TMP_Text label;

        [SerializeField] private float maxBubbleWidth = 300f;
        [SerializeField] private float bubblePaddingX = 32f;

        private static readonly Color MineColor = new Color(0.16f, 0.45f, 0.92f);   // blue
        private static readonly Color TheirsColor = new Color(0.26f, 0.27f, 0.31f); // gray

        private bool _mine;
        private string _message;
        private bool _read;

        public bool IsMine => _mine;

        public void Bind(string message, bool mine, bool read) {
            _mine = mine;
            _message = message ?? string.Empty;
            _read = read;

            if (layout != null) layout.childAlignment = mine ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            if (bubble != null) bubble.color = mine ? MineColor : TheirsColor;
            Render();
        }

        public void SetRead(bool read) {
            if (!_mine || _read == read) return;
            _read = read;
            Render();
        }

        private void Render() {
            if (label == null) return;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.text = _mine
                ? $"{_message}\n<size=60%><color=#CFE0FF>{(_read ? "Lu" : "Non lu")}</color></size>"
                : _message;

            // Clamp only the bubble WIDTH (so short messages stay small, long ones
            // wrap at maxBubbleWidth). Height is left to the bubble's ContentSizeFitter
            // (it measures multi-line rich text correctly). The row's HorizontalLayoutGroup
            // must NOT control the bubble size — it only aligns it left/right — otherwise
            // it fights the ContentSizeFitter and under-measures the height.
            if (bubbleLayout != null) {
                float w = Mathf.Min(label.GetPreferredValues(label.text, maxBubbleWidth, 0f).x, maxBubbleWidth);
                bubbleLayout.preferredWidth = w + bubblePaddingX;
            }
        }
    }
}
