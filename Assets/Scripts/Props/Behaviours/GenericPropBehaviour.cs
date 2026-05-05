using System.Linq;
using Sim.Building;
using Sim.Scriptables;
using UnityEngine;

/// <summary>
/// Client-side behaviour for generic furniture props (shelves, lamps, tables…).
/// Reads PropStateHeader from the payload and applies isBuilt + presetId to PropsRenderer.
/// </summary>
[RequireComponent(typeof(PropsRenderer))]
public class GenericPropBehaviour : MonoBehaviour, IPropBehaviour {
    [SerializeField] private PropsConfig configuration;

    private PropsRenderer _renderer;

    private void Awake() {
        _renderer = GetComponent<PropsRenderer>();
    }

    public void ApplyState(PropType type, byte[] payload) {
        PropStateHeader header = PropStateHeader.ReadFrom(payload);

        _renderer.SetBuiltState(header.IsBuilt);

        if (header.PresetId >= 0 && configuration?.Presets != null) {
            PropsPreset preset = configuration.Presets.FirstOrDefault(p => p.ID == header.PresetId);
            if (preset != null) _renderer.SetPreset(preset);
        }
    }
}
