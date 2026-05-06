using UnityEngine;

/// <summary>
/// Server-side source for Dispenser props (city shops).
/// State is just the header — the catalog comes from the ScriptableObject.
/// Reads defaultPresetId from sibling PropBehaviourBase if present.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DispenserPropSource : ServerPropSource {
    public override PropType Type => PropType.Dispenser;

    private PropBehaviourBase _behaviour;

    private void Awake() {
        _behaviour = GetComponent<PropBehaviourBase>();
    }

    public override byte[] GetInitialState() {
        int presetId = _behaviour != null ? _behaviour.DefaultPresetId : -1;
        return new DispenserState {
            Header = new PropStateHeader { IsBuilt = true, PresetId = presetId }
        }.Serialize();
    }
}
