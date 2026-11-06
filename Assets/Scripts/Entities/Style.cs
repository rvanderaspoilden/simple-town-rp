using System;

/// <summary>
/// Persisted character appearance (players AND NPCs). Characters are configured
/// exclusively through the Advanced People System 2 pack: <see cref="advanced"/>
/// holds the full APS2 <c>CharacterCustomizationSetup</c> serialized as JSON
/// (elements, blendshapes, body colors, height, head size). <see cref="gender"/>
/// selects which APS2 CharacterSettings (Male/Female) the setup applies to.
/// Empty <see cref="advanced"/> (legacy documents) falls back to the settings'
/// default elements — deterministic, so every client renders the same look.
/// </summary>
[Serializable]
public struct Style {
    public Gender gender;

    public string advanced;
}
