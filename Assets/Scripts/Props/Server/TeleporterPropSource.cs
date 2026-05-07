using UnityEngine;

/// <summary>
/// Server-side source for teleporter props (hall elevators + city main elevator).
/// State is header-only — the teleporter has no persistent visual state beyond built/preset.
/// The actual use logic is handled via C2S_TeleporterUse → PropInteractionDispatcher.HandleTeleporterUse.
/// </summary>
[RequireComponent(typeof(PropIdentity))]
[RequireComponent(typeof(TeleporterBehaviour))]
public class TeleporterPropSource : ServerPropSource {
    public override PropType Type => PropType.Teleporter;

    public override byte[] GetInitialState() =>
        new GenericPropState { Header = PropStateHeader.Default }.Serialize();
}
