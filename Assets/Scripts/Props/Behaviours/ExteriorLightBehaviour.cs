using UnityEngine;

/// <summary>
/// Client-side day/night control for exterior luminaires (street lamps).
///
/// Drives the emissive material glow ON at night and OFF during the day using the
/// shared in-game clock (<see cref="TimeManager.CurrentTime"/>, same source as
/// <see cref="MeteoManager"/>). The lamp has no Unity <see cref="Light"/> component:
/// the light is purely the emissive mesh, so we toggle <c>_EmissionColor</c> via a
/// <see cref="MaterialPropertyBlock"/> — no material instancing, no GC, and other
/// luminaires sharing the same material asset are unaffected.
///
/// Network-wise this stays a Generic prop (handled by <see cref="GenericPropSource"/>
/// server-side); the clock is already synchronised by Mirror, so every client reaches
/// the same state without any per-prop networking.
/// </summary>
public class ExteriorLightBehaviour : PropBehaviourBase
{
    [Header("Exterior Light")]
    [Tooltip("Emissive renderers driven on/off. Auto-filled from children with an emissive material if left empty.")]
    [SerializeField] private Renderer[] emissiveRenderers;

    [Tooltip("Hour (0-23) at which the lamp turns ON.")]
    [SerializeField, Range(0, 23)] private int onHour = 20;

    [Tooltip("Hour (0-23) at which the lamp turns OFF.")]
    [SerializeField, Range(0, 23)] private int offHour = 6;

    [Tooltip("When true, use 'Night Emission' below instead of the material's authored _EmissionColor.")]
    [SerializeField] private bool overrideNightColor = false;

    [Tooltip("Emission color/intensity used at night when 'Override Night Color' is enabled.")]
    [SerializeField, ColorUsage(true, true)] private Color nightEmission = Color.black;

    [Tooltip("-1 = use the real in-game clock. 0-23 forces an hour for editor preview.")]
    [SerializeField, Range(-1, 23)] private int debugForceHour = -1;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock _mpb;
    private Color _onColor = Color.black;
    private bool? _isLit; // null until first applied, so the first frame always pushes a state.

    protected override void Awake()
    {
        base.Awake();
        _mpb = new MaterialPropertyBlock();

        if (emissiveRenderers == null || emissiveRenderers.Length == 0)
            emissiveRenderers = CollectEmissiveRenderers();

        _onColor = overrideNightColor ? nightEmission : ReadAuthoredEmission();
    }

    private void Update()
    {
        int hour = debugForceHour >= 0 ? debugForceHour : TimeManager.CurrentTime.Hours;
        bool lit = IsNightHour(hour);
        if (_isLit == lit) return;

        _isLit = lit;
        ApplyEmission(lit ? _onColor : Color.black);
    }

    /// <summary>Night window, wrapping past midnight when onHour &gt; offHour (e.g. 20 → 6).</summary>
    private bool IsNightHour(int hour)
    {
        if (onHour == offHour) return false;
        return onHour < offHour
            ? (hour >= onHour && hour < offHour)
            : (hour >= onHour || hour < offHour);
    }

    private void ApplyEmission(Color color)
    {
        if (emissiveRenderers == null) return;
        foreach (Renderer r in emissiveRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, color);
            r.SetPropertyBlock(_mpb);
        }
    }

    private Color ReadAuthoredEmission()
    {
        if (emissiveRenderers != null)
        {
            foreach (Renderer r in emissiveRenderers)
            {
                if (r == null) continue;
                Material mat = r.sharedMaterial;
                if (mat != null && mat.HasProperty(EmissionColorId))
                    return mat.GetColor(EmissionColorId);
            }
        }
        return Color.black;
    }

    private Renderer[] CollectEmissiveRenderers()
    {
        var result = new System.Collections.Generic.List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            Material mat = r.sharedMaterial;
            if (mat != null && mat.IsKeywordEnabled("_EMISSION"))
                result.Add(r);
        }
        return result.ToArray();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        emissiveRenderers = CollectEmissiveRenderers();
    }
#endif
}
