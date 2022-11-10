using System;
using Mirror;
using UnityEngine;

namespace Network.Messages {
    [Serializable]
    public struct CreateBuildingMessage : NetworkMessage {
        public int buildingId;
        public CustomizedMaterialPart[] customizedMaterialParts;
    }

    [Serializable]
    public struct CustomizedMaterialPart {
        public int id;
        public Color color;
    }
}