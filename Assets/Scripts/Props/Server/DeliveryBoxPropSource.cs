using UnityEngine;

/// <summary>
/// Server-side source for DeliveryBox props. Initial state has deliveryCount=0;
/// the actual count is fetched via REST after spawn (see ApartmentController.InstantiateLevel
/// → PropInteractionDispatcher.RefreshDeliveryBoxCount).
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DeliveryBoxPropSource : ServerPropSource {
    public override PropType Type => PropType.DeliveryBox;

    public override byte[] GetInitialState() =>
        new DeliveryBoxState { Header = PropStateHeader.Default, DeliveryCount = 0 }.Serialize();
}
