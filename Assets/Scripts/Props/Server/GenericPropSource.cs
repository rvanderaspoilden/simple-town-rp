using UnityEngine;

/// <summary>
/// Server-side source for generic furniture props (no special server logic).
/// The header (isBuilt + presetId) is the only meaningful state.
/// Reads defaultPresetId from sibling PropBehaviourBase if present.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class GenericPropSource : ServerPropSource {
    public override PropType Type => PropType.Generic;

    private PropBehaviourBase _behaviour;

    private void Awake() {
        _behaviour = GetComponent<PropBehaviourBase>();
    }

    public override byte[] GetInitialState() {
        int presetId = _behaviour != null ? _behaviour.DefaultPresetId : -1;
        return new GenericPropState {
            Header = new PropStateHeader { IsBuilt = true, PresetId = presetId }
        }.Serialize();
    }
}
