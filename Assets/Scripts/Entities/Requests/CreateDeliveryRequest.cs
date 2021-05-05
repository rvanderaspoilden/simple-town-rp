using System;
using Mirror;
using Sim.Entities;

[Serializable]
public struct CreateDeliveryRequest : NetworkMessage
{
    public string recipientId;

    public DeliveryType type;

    public int paintConfigId;

    public int propsConfigId;

    public float[] color;

    public int propsPresetId;
}
