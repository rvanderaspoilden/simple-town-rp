using System;
using Mirror;
using UnityEngine;

namespace Network.Messages {
    [Serializable]
    public struct CreateBuildingMessage : NetworkMessage {
        public int buildingId;
        public Color mainColor;
        public Color firstStoreColor;
        public Color secondStoreColor;
        public Color barColor;
    }
}