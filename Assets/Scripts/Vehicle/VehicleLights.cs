using UnityEngine;

/// <summary>
/// Visuel des feux d'un véhicule — purement client, aucun réseau (l'état est répliqué par
/// <see cref="VehicleController"/> via un SyncVar, qui appelle ces setters dans son hook, donc
/// tous les clients voient le même rendu).
///
///   - Phares avant : Spot lights URP allumés/éteints + optiques avant qui s'illuminent.
///   - Feux stop arrière : optiques rouges qui s'illuminent au freinage.
///
/// L'illumination des optiques se fait via <see cref="MaterialPropertyBlock"/> (`_EmissionColor`),
/// donc SANS instancier de matériau : le matériau d'optique doit avoir l'ÉMISSION ACTIVÉE (keyword
/// `_EMISSION` ON) avec une couleur de base noire ; on ne fait que moduler la couleur (noir = éteint).
/// </summary>
public class VehicleLights : MonoBehaviour {
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Phares avant")]
    [Tooltip("Spot lights URP des phares (projection du cône lumineux).")]
    [SerializeField] private Light[] headlightSpots;
    [Tooltip("Meshes d'optique avant qui s'illuminent avec les phares.")]
    [SerializeField] private Renderer[] headlightEmissive;
    [Tooltip("Couleur d'émission des optiques avant quand allumées.")]
    [SerializeField] private Color headlightEmission = new Color(1f, 0.95f, 0.85f) * 2f;

    [Header("Feux stop arrière")]
    [Tooltip("Meshes d'optique arrière (feux stop), illuminés au freinage.")]
    [SerializeField] private Renderer[] brakeEmissive;
    [Tooltip("Point lights rouges arrière (optionnel).")]
    [SerializeField] private Light[] brakeLights;
    [Tooltip("Couleur d'émission des feux stop quand allumés.")]
    [SerializeField] private Color brakeEmission = new Color(1f, 0.1f, 0.05f) * 3f;

    private MaterialPropertyBlock _mpb;

    private void Awake() {
        _mpb = new MaterialPropertyBlock();
        SetHeadlights(false);
        SetBrake(false);
    }

    /// <summary>Allume/éteint les phares avant (spots + optiques).</summary>
    public void SetHeadlights(bool on) {
        if (headlightSpots != null)
            foreach (Light l in headlightSpots) if (l != null) l.enabled = on;
        ApplyEmission(headlightEmissive, on ? headlightEmission : Color.black);
    }

    /// <summary>Allume/éteint les feux stop arrière (optiques + point lights optionnels).</summary>
    public void SetBrake(bool on) {
        ApplyEmission(brakeEmissive, on ? brakeEmission : Color.black);
        if (brakeLights != null)
            foreach (Light l in brakeLights) if (l != null) l.enabled = on;
    }

    private void ApplyEmission(Renderer[] renderers, Color color) {
        if (renderers == null) return;
        foreach (Renderer r in renderers) {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, color);
            r.SetPropertyBlock(_mpb);
        }
    }
}
