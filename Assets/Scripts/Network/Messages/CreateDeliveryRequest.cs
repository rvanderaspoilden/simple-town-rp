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

    /// <summary>
    /// UUID of the materialized prop in the new persistence model. Set server-side
    /// after a successful POST /props at buy time — clients should leave this null.
    /// </summary>
    public string propId;
}
