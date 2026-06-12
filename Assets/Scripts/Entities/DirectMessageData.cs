using System;
using System.Collections.Generic;

namespace Sim.Entities {
    /// <summary>One direct message ("SMS"). Mirrors a backend direct_messages row.</summary>
    [Serializable]
    public class DirectMessageData {
        public string senderId;
        public string recipientId;
        public string message;
        public bool read;
        public long createdAt; // epoch seconds
    }

    [Serializable]
    public class ConversationResponse {
        public List<DirectMessageData> messages = new List<DirectMessageData>();
    }

    [Serializable]
    public class UnreadCount {
        public string contactId;
        public int count;
    }

    [Serializable]
    public class UnreadResponse {
        public List<UnreadCount> unread = new List<UnreadCount>();
    }

    /// <summary>Body for POST /direct-messages (sent by the Unity server).</summary>
    [Serializable]
    public class SendDmBody {
        public string senderId;
        public string recipientId;
        public string message;
    }
}
