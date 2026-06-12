using System;
using Mirror;
using Sim.Logging;

namespace Sim {
    /// <summary>
    /// Wires the SMS (direct message) flow into Mirror's lifecycle (pattern of
    /// AcquaintanceSystemBootstrap). Server persists each message and relays it to
    /// the recipient if online; client shows a notification (or appends to the open
    /// conversation) and refreshes the unread badge.
    /// </summary>
    public static class SmsSystemBootstrap {
        private const int MaxLength = 500;

        // ── Server ──────────────────────────────────────────────────────────
        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_SendSms>(OnSendSms);
            NetworkServer.RegisterHandler<C2S_SmsMarkRead>(OnMarkRead);
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_SendSms>();
            NetworkServer.UnregisterHandler<C2S_SmsMarkRead>();
        }

        private static void OnMarkRead(NetworkConnectionToClient conn, C2S_SmsMarkRead msg) {
            if (conn?.identity == null) return;
            PlayerController reader = conn.identity.GetComponent<PlayerController>();
            if (reader?.CharacterData == null) return;

            string readerId = reader.CharacterData.Id;
            string otherId = msg.otherCharacterId;
            if (string.IsNullOrEmpty(readerId) || string.IsNullOrEmpty(otherId) || readerId == otherId) return;

            // Live read-receipt to the original sender (persistence is handled by the
            // reader's REST MarkConversationRead call).
            FindConnectionByCharacter(otherId)?.Send(new S2C_SmsRead { readerCharacterId = readerId });
        }

        private static void OnSendSms(NetworkConnectionToClient conn, C2S_SendSms msg) {
            if (conn?.identity == null) return;
            PlayerController sender = conn.identity.GetComponent<PlayerController>();
            if (sender?.CharacterData == null) return;

            string text = (msg.text ?? string.Empty).Trim();
            if (text.Length == 0) return;
            if (text.Length > MaxLength) text = text.Substring(0, MaxLength);

            string senderId = sender.CharacterData.Id;
            string senderName = sender.CharacterData.Identity.FullName;
            string recipientId = msg.recipientCharacterId;
            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(recipientId) || senderId == recipientId) return;

            // Persist (always), then deliver live if the recipient is connected.
            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.SendDirectMessageCoroutine(senderId, recipientId, text));

            NetworkConnectionToClient recipientConn = FindConnectionByCharacter(recipientId);
            recipientConn?.Send(new S2C_SmsReceived {
                senderCharacterId = senderId,
                senderName = senderName,
                message = text,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });

            GameLogger.Network.Info("SmsSent {From} {To}", senderId, recipientId);
        }

        private static NetworkConnectionToClient FindConnectionByCharacter(string characterId) {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn?.identity == null) continue;
                PlayerController player = conn.identity.GetComponent<PlayerController>();
                if (player?.CharacterData != null && player.CharacterData.Id == characterId) return conn;
            }
            return null;
        }

        // ── Client ──────────────────────────────────────────────────────────
        public static void OnClientStart() {
            NetworkClient.RegisterHandler<S2C_SmsReceived>(OnSmsReceived);
            NetworkClient.RegisterHandler<S2C_SmsRead>(OnSmsRead);
        }

        public static void OnClientStop() {
            NetworkClient.UnregisterHandler<S2C_SmsReceived>();
            NetworkClient.UnregisterHandler<S2C_SmsRead>();
        }

        private static void OnSmsRead(S2C_SmsRead msg) {
            if (SmsConversationUI.Instance != null && SmsConversationUI.Instance.IsOpenFor(msg.readerCharacterId)) {
                SmsConversationUI.Instance.MarkMineRead();
            }
        }

        private static void OnSmsReceived(S2C_SmsReceived msg) {
            bool conversationOpen = SmsConversationUI.Instance != null
                && SmsConversationUI.Instance.IsOpenFor(msg.senderCharacterId);

            if (conversationOpen) {
                SmsConversationUI.Instance.AppendIncoming(msg.senderCharacterId, msg.message);
                // Reading happens because the conversation is open: persist (REST) and
                // relay a live read-receipt to the sender.
                string localId = PlayerController.Local?.CharacterData?.Id;
                if (!string.IsNullOrEmpty(localId)) {
                    ApiManager.Instance.MarkConversationRead(localId, msg.senderCharacterId);
                    NetworkClient.Send(new C2S_SmsMarkRead { otherCharacterId = msg.senderCharacterId });
                }
            } else {
                NotificationManager.Instance?.AddNotification($"SMS de {msg.senderName}", NotificationType.SUPPORT);
                // Refresh unread badges (Contacts app listens to OnUnreadRetrieved).
                string localId = PlayerController.Local?.CharacterData?.Id;
                if (!string.IsNullOrEmpty(localId)) ApiManager.Instance.RetrieveUnread(localId);
            }
        }
    }
}
