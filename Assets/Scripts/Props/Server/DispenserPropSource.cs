using UnityEngine;

/// <summary>
/// Server-side source for Dispenser props (city shops).
/// State is just the header — the catalog comes from the ScriptableObject.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
public class DispenserPropSource : ServerPropSource {
    public override PropType Type => PropType.Dispenser;

    public override byte[] GetInitialState() =>
        new DispenserState { Header = PropStateHeader.Default }.Serialize();
}
