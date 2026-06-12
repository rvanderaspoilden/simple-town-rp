using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// Simple data-binding component on a Contact Row template. The list view
    /// clones the template per contact, then calls Bind() to fill the visuals.
    /// </summary>
    public class ContactRowUI : MonoBehaviour {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image onlineStatus;       // colored dot: green online / red offline
        [SerializeField] private GameObject messageNotif;  // shown when unread > 0

        private static readonly Color OnlineColor = new Color(0.32f, 0.80f, 0.32f);  // #51CC51
        private static readonly Color OfflineColor = new Color(0.85f, 0.25f, 0.25f); // #D94040

        public void Bind(string contactName, bool isOnline, bool hasUnread) {
            if (label != null) label.text = contactName;
            if (onlineStatus != null) onlineStatus.color = isOnline ? OnlineColor : OfflineColor;
            if (messageNotif != null) messageNotif.SetActive(hasUnread);
        }
    }
}
