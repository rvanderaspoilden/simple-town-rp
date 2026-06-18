using System;
using System.Collections.Generic;
using Sim;
using Sim.Entities;
using Sim.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phone app: the player's contacts. Two sub-views authored in the prefab:
/// the list view (search + scroll + row template) and the SMS conversation
/// view (handled by SmsConversationUI). Rows are cloned from the template
/// inside the scroll content, not built procedurally. Contacts with unread
/// messages are sorted to the top.
/// </summary>
public class ContactsUI : PhoneApplicationUI {
    [SerializeField] private GameObject listView;
    [SerializeField] private GameObject conversationView;
    [SerializeField] private GameObject callView;
    [SerializeField] private TMP_InputField searchInput;
    [SerializeField] private GameObject rowTemplate;
    [SerializeField] private RectTransform scrollContent;

    // Resolved lazily so call network handlers can drive the view even before the
    // Contacts app was ever opened (it starts inactive in the prefab).
    private static ContactsUI _instance;
    public static ContactsUI Instance {
        get {
            if (_instance != null) return _instance;
            _instance = FindFirstObjectByType<ContactsUI>(FindObjectsInactive.Include);
            if (_instance != null) return _instance;
            foreach (ContactsUI ui in Resources.FindObjectsOfTypeAll<ContactsUI>()) {
                if (ui != null && ui.gameObject.scene.IsValid()) { _instance = ui; break; }
            }
            return _instance;
        }
    }

    private Sim.CallUI _callUI;
    private Sim.CallUI CallView {
        get {
            if (_callUI == null && callView != null) _callUI = callView.GetComponent<Sim.CallUI>();
            return _callUI;
        }
    }

    private readonly Dictionary<string, int> _unread = new Dictionary<string, int>();
    private readonly List<GameObject> _rows = new List<GameObject>();
    private string _search = string.Empty;

    private void Awake() {
        _instance = this;
        if (rowTemplate != null) rowTemplate.SetActive(false);
    }

    private void OnEnable() {
        ApiManager.OnRelationshipsRetrieved += OnRelationshipsRefreshed;
        ApiManager.OnUnreadRetrieved += OnUnreadRefreshed;
        SmsConversationUI.OnClosed += ShowList;
        ClientRelationshipManager.OnPresenceChanged += OnPresenceChanged;
        ClientRelationshipManager.OnRelationshipRemoved += OnRelationshipRemoved;

        if (searchInput != null) {
            searchInput.onValueChanged.AddListener(OnSearchChanged);
            searchInput.text = string.Empty;
        }

        string localId = PlayerController.Local?.CharacterData?.Id;
        if (!string.IsNullOrEmpty(localId) && ApiManager.Instance != null) {
            ApiManager.Instance.RetrieveRelationships(localId);
            ApiManager.Instance.RetrieveUnread(localId);
        }

        ShowList();
    }

    private void OnDisable() {
        ApiManager.OnRelationshipsRetrieved -= OnRelationshipsRefreshed;
        ApiManager.OnUnreadRetrieved -= OnUnreadRefreshed;
        SmsConversationUI.OnClosed -= ShowList;
        ClientRelationshipManager.OnPresenceChanged -= OnPresenceChanged;
        ClientRelationshipManager.OnRelationshipRemoved -= OnRelationshipRemoved;
        if (searchInput != null) searchInput.onValueChanged.RemoveListener(OnSearchChanged);
    }

    private void OnDestroy() {
        if (_instance == this) _instance = null;
    }

    private void OnPresenceChanged(string contactId, bool online) {
        // Live presence delta on a known contact → rebuild so the dot updates.
        if (listView != null && listView.activeSelf) Rebuild();
    }

    private void OnRelationshipRemoved(string contactId) {
        // If we're viewing the removed contact's conversation, drop back to the list.
        if (SmsConversationUI.Instance != null && SmsConversationUI.Instance.IsOpenFor(contactId)) ShowList();
        Rebuild();
    }

    public override void Back() {
        // During a call the only way out is the call buttons (hangup/decline).
        if (callView != null && callView.activeSelf) return;
        if (conversationView != null && conversationView.activeSelf) {
            ShowList();
            return;
        }
        PhoneControllerUI.Instance?.BackToHome();
    }

    // ── Call sub-view (driven by CallSystemBootstrap) ──────────────────────
    public void ShowOutgoingCall(string peerId, string peerName) {
        ShowCallView();
        CallView?.ShowOutgoing(peerId, peerName);
    }

    public void ShowIncomingCall(string callerId, string callerName, uint callerNetId) {
        ShowCallView();
        CallView?.ShowIncoming(callerId, callerName, callerNetId);
    }

    public void ShowActiveCall() {
        ShowCallView();
        CallView?.ShowActive();
    }

    public void EndCall(CallEndReason reason) {
        CallView?.HandleEnded(reason);
        ShowList();
    }

    public void ReturnToList() => ShowList();

    private void ShowCallView() {
        if (listView != null) listView.SetActive(false);
        if (conversationView != null) conversationView.SetActive(false);
        if (callView != null) callView.SetActive(true);
    }

    private void OnRelationshipsRefreshed(List<RelationshipData> _) => Rebuild();

    private void OnUnreadRefreshed(List<UnreadCount> counts) {
        _unread.Clear();
        if (counts != null) {
            foreach (UnreadCount u in counts) {
                if (!string.IsNullOrEmpty(u.contactId)) _unread[u.contactId] = u.count;
            }
        }
        Rebuild();
    }

    private void OnSearchChanged(string value) {
        _search = (value ?? string.Empty).Trim();
        Rebuild();
    }

    private void ShowList() {
        if (listView != null) listView.SetActive(true);
        if (conversationView != null) conversationView.SetActive(false);
        if (callView != null) callView.SetActive(false);
    }

    /// <summary>Open the SMS conversation with the given contact, switching from
    /// the list view. Safe to call from outside (e.g. when the player taps an
    /// SMS notification) — assumes the Contacts app itself was already brought
    /// to the foreground via <see cref="PhoneControllerUI.ForceOpenApp"/>.</summary>
    public void OpenConversation(string contactId, string contactName) {
        ShowConversation(contactId, contactName);
    }

    private void ShowConversation(string contactId, string contactName) {
        if (listView != null) listView.SetActive(false);
        if (conversationView != null) {
            SmsConversationUI sms = conversationView.GetComponent<SmsConversationUI>();
            if (sms != null) sms.Open(contactId, contactName);
            else conversationView.SetActive(true);
        }
    }

    private void Rebuild() {
        if (rowTemplate == null || scrollContent == null) return;

        foreach (GameObject row in _rows) if (row != null) Destroy(row);
        _rows.Clear();

        string filter = _search.ToLowerInvariant();

        // Snapshot + sort: unread first (desc by count), then alphabetical by name.
        List<(string id, string name, bool online)> entries = new List<(string, string, bool)>();
        foreach (KeyValuePair<string, RelationshipEntry> kv in ClientRelationshipManager.Instance.All) {
            RelationshipEntry e = kv.Value;
            if (e.State != RelationshipState.Contact) continue;
            string name = string.IsNullOrEmpty(e.FullName) ? "Broz" : e.FullName;
            if (filter.Length > 0 && name.ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0) continue;
            entries.Add((kv.Key, name, e.Online));
        }
        entries.Sort((a, b) => {
            int ua = _unread.TryGetValue(a.id, out int na) ? na : 0;
            int ub = _unread.TryGetValue(b.id, out int nb) ? nb : 0;
            if (ua != ub) return ub.CompareTo(ua); // unread first, higher counts first
            return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        });

        foreach ((string id, string name, bool online) in entries) {
            bool hasUnread = _unread.TryGetValue(id, out int n) && n > 0;
            CreateRow(id, name, online, hasUnread);
        }
    }

    private void CreateRow(string characterId, string contactName, bool isOnline, bool hasUnread) {
        GameObject row = Instantiate(rowTemplate, scrollContent);
        row.SetActive(true);

        ContactRowUI binder = row.GetComponent<ContactRowUI>();
        if (binder != null) binder.Bind(contactName, isOnline, hasUnread);

        Button btn = row.GetComponent<Button>();
        if (btn != null) {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => ShowConversation(characterId, contactName));
        }

        _rows.Add(row);
    }
}
