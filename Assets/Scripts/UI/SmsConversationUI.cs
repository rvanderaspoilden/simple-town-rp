using System;
using System.Collections.Generic;
using Mirror;
using Sim.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// SMS conversation view. AUTHORED in HUD Manager.prefab (not procedural) as
    /// the second sub-view of the Contacts app. ContactsUI toggles between the
    /// list view and this conversation view; Hide() fires OnClosed so the list
    /// view can come back without coupling the two scripts directly.
    /// </summary>
    public class SmsConversationUI : MonoBehaviour {
        /// <summary>Fired when the modal is closed (Close button or programmatic Hide).
        /// ContactsUI listens to this to swap back to the list view.</summary>
        public static event Action OnClosed;

        private static SmsConversationUI _instance;
        /// <summary>Resolved lazily so SmsSystemBootstrap can check IsOpenFor on
        /// incoming SMS even before the conversation view was ever activated.</summary>
        public static SmsConversationUI Instance {
            get {
                if (_instance != null) return _instance;
                _instance = FindFirstObjectByType<SmsConversationUI>(FindObjectsInactive.Include);
                if (_instance != null) return _instance;
                foreach (SmsConversationUI ui in Resources.FindObjectsOfTypeAll<SmsConversationUI>()) {
                    if (ui != null && ui.gameObject.scene.IsValid()) { _instance = ui; break; }
                }
                return _instance;
            }
        }

        [SerializeField] private TMP_Text headerNameText;
        [SerializeField] private TMP_Text headerStatusText; // "En ligne" / "Hors ligne"
        [SerializeField] private Button callButton;       // placeholder this lot (no-op)
        [SerializeField] private Button seeButton;         // opens the contact's identity card

        private static readonly Color OnlineColor = new Color(0.32f, 0.80f, 0.32f);
        private static readonly Color OfflineColor = new Color(0.85f, 0.25f, 0.25f);
        [SerializeField] private RectTransform scrollContent;
        [SerializeField] private GameObject messageRowTemplate;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private ScrollRect messagesScroll;

        private string _currentContactId;
        private bool _wired;
        private readonly List<SmsMessageRowUI> _rows = new List<SmsMessageRowUI>();

        private void Awake() {
            _instance = this;
            Wire();
            ClientRelationshipManager.OnPresenceChanged += OnContactPresenceChanged;
        }

        private void OnContactPresenceChanged(string contactId, bool online) {
            if (contactId != _currentContactId) return;
            ApplyOnlineState(online);
        }

        // Header dot color + call button gating share the same online state.
        // Calling an offline contact would just bounce back "Unavailable".
        private void ApplyOnlineState(bool online) {
            if (headerStatusText != null) {
                headerStatusText.text = online ? "En ligne" : "Hors ligne";
                headerStatusText.color = online ? OnlineColor : OfflineColor;
            }
            if (callButton != null) callButton.interactable = online;
        }

        // Listeners are wired on first activation (Awake doesn't run while the GO
        // starts inactive in the prefab).
        private void Wire() {
            if (_wired) return;
            _wired = true;
            if (messageRowTemplate != null) messageRowTemplate.SetActive(false);
            if (sendButton != null) sendButton.onClick.AddListener(Send);
            if (callButton != null) callButton.onClick.AddListener(StartCall);
            if (seeButton != null) seeButton.onClick.AddListener(SeeIdentity);
            if (inputField != null) inputField.onSubmit.AddListener(_ => Send());
        }

        private void OnDestroy() {
            if (_instance == this) _instance = null;
            ApiManager.OnConversationRetrieved -= OnConversationRetrieved;
            ClientRelationshipManager.OnPresenceChanged -= OnContactPresenceChanged;
        }

        public bool IsOpenFor(string contactId) =>
            gameObject.activeSelf && _currentContactId == contactId;

        public void Open(string contactId, string contactName) {
            _currentContactId = contactId;
            gameObject.SetActive(true);
            Wire();
            if (headerNameText != null) headerNameText.text = contactName;
            bool online = ClientRelationshipManager.Instance.TryGet(contactId, out RelationshipEntry e) && e.Online;
            ApplyOnlineState(online);
            ClearRows();

            ApiManager.OnConversationRetrieved -= OnConversationRetrieved;
            ApiManager.OnConversationRetrieved += OnConversationRetrieved;

            string localId = PlayerController.Local != null && PlayerController.Local.CharacterData != null
                ? PlayerController.Local.CharacterData.Id : null;
            if (!string.IsNullOrEmpty(localId)) {
                ApiManager.Instance.RetrieveConversation(localId, contactId);
                ApiManager.Instance.MarkConversationRead(localId, contactId);
                // Relay a live read-receipt so the contact sees their bubbles flip to "Lu".
                NetworkClient.Send(new C2S_SmsMarkRead { otherCharacterId = contactId });
            }
        }

        /// <summary>The open contact just read our sent messages → flip them to "Lu".</summary>
        public void MarkMineRead() {
            foreach (SmsMessageRowUI row in _rows) {
                if (row != null && row.IsMine) row.SetRead(true);
            }
        }

        public void Hide() {
            ApiManager.OnConversationRetrieved -= OnConversationRetrieved;
            _currentContactId = null;
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        public void AppendIncoming(string senderId, string message) {
            if (senderId != _currentContactId) return;
            // Modal is open → recipient sees the bubble immediately, so it counts as read.
            AddRow(new DirectMessageData {
                senderId = senderId,
                message = message,
                read = true,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            }, mine: false);
            ScrollToBottom();
        }

        private void OnConversationRetrieved(string otherId, List<DirectMessageData> messages) {
            if (otherId != _currentContactId) return;
            ClearRows();
            string localId = PlayerController.Local != null && PlayerController.Local.CharacterData != null
                ? PlayerController.Local.CharacterData.Id : null;
            foreach (DirectMessageData m in messages) {
                AddRow(m, mine: m.senderId == localId);
            }
            ScrollToBottom();
        }

        private void SeeIdentity() {
            if (string.IsNullOrEmpty(_currentContactId)) return;
            IdentityCardUI.Instance?.ShowFor(_currentContactId);
        }

        private void StartCall() {
            if (string.IsNullOrEmpty(_currentContactId)) return;
            string name = headerNameText != null ? headerNameText.text : null;
            ContactsUI.Instance?.ShowOutgoingCall(_currentContactId, name);
            NetworkClient.Send(new C2S_CallInvite { targetCharacterId = _currentContactId });
        }

        private void Send() {
            if (inputField == null || string.IsNullOrEmpty(_currentContactId)) return;
            string text = (inputField.text ?? string.Empty).Trim();
            if (text.Length == 0) return;

            string localId = PlayerController.Local?.CharacterData?.Id;
            AddRow(new DirectMessageData {
                senderId = localId,
                message = text,
                read = false, // recipient hasn't read it yet
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            }, mine: true); // optimistic echo

            NetworkClient.Send(new C2S_SendSms { recipientCharacterId = _currentContactId, text = text });
            ScrollToBottom();

            inputField.text = string.Empty;
            inputField.ActivateInputField();
        }

        private void AddRow(DirectMessageData data, bool mine) {
            if (messageRowTemplate == null || scrollContent == null) return;
            GameObject row = Instantiate(messageRowTemplate, scrollContent);
            row.SetActive(true);
            SmsMessageRowUI ui = row.GetComponent<SmsMessageRowUI>();
            if (ui != null) ui.Bind(data.message, mine, data.read);
            _rows.Add(ui);
        }

        private void ClearRows() {
            foreach (SmsMessageRowUI r in _rows) if (r != null) Destroy(r.gameObject);
            _rows.Clear();
        }

        // Land on the newest message (bottom). The bubble/row/content sizing is a
        // chain of nested ContentSizeFitters; Canvas.ForceUpdateCanvases drives them
        // through the CanvasUpdateRegistry, which can rebuild a parent before its
        // child and leave the VerticalLayoutGroup reading stale row heights on the
        // first pass (rows overlap until a CSF is toggled). ForceRebuildLayoutImmediate
        // rebuilds the subtree depth-first (children first) so heights are correct in
        // one synchronous call. We rebuild the content, then jump the scroll position.
        private void ScrollToBottom() {
            if (scrollContent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
            if (messagesScroll == null) return;
            Canvas.ForceUpdateCanvases();
            messagesScroll.verticalNormalizedPosition = 0f;
        }
    }
}
