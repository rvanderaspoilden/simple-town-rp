using UnityEngine;

/// <summary>
/// Server-side source for DeliveryBox props. Initial state has deliveryCount=0;
/// the actual count is fetched via REST after spawn (see ApartmentController.InstantiateLevel
/// → PropInteractionDispatcher.RefreshDeliveryBoxCount).
/// Reads defaultPresetId from sibling PropBehaviourBase if present.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DeliveryBoxPropSource : ServerPropSource {
    public override PropType Type => PropType.DeliveryBox;

    private PropBehaviourBase _behaviour;

    private void Awake() {
        _behaviour = GetComponent<PropBehaviourBase>();
    }

    public override byte[] GetInitialState() {
        int presetId = _behaviour != null ? _behaviour.DefaultPresetId : -1;
        return new DeliveryBoxState {
            Header = new PropStateHeader { IsBuilt = true, PresetId = presetId },
            DeliveryCount = 0
        }.Serialize();
    }
}
