using UnityEngine;

/// <summary>
/// Server-side source for Package props.
/// State contains header + PropsConfigId (the content of the package).
/// Reads defaultPresetId from sibling PropBehaviourBase if present.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class PackagePropSource : ServerPropSource {
    public override PropType Type => PropType.Package;

    private PackageBehaviour _packageBehaviour;

    private void Awake() {
        _packageBehaviour = GetComponent<PackageBehaviour>();
    }

    public override byte[] GetInitialState() {
        int presetId = _packageBehaviour != null ? _packageBehaviour.DefaultPresetId : -1;
        int propsConfigId = _packageBehaviour != null && _packageBehaviour.GetPropsConfigInside() != null
            ? _packageBehaviour.GetPropsConfigInside().GetId()
            : 0;

        return new PackageState {
            Header        = new PropStateHeader { IsBuilt = true, PresetId = presetId },
            PropsConfigId = propsConfigId
        }.Serialize();
    }
}
