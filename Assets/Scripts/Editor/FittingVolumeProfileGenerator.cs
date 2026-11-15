using Sim.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Crée (ou remet à jour) le VolumeProfile « cabine d'essayage » — MODE PORTRAIT : pendant
/// l'essayage le personnage est rendu par une caméra overlay hors post-processing, et ce volume
/// ne touche donc que le décor derrière lui : flouté (DoF Bokeh réglé pour que TOUT le monde soit
/// hors focus, quelle que soit la profondeur), assombri et désaturé, avec une vignette.
/// Puis l'assigne au <see cref="FittingRoomUI"/> du prefab HUD Manager.
///
/// Idempotent : les overrides manquants sont créés, les valeurs réécrites (GUID conservé). Les
/// réglages restent ajustables dans l'inspecteur du profil ; relancer l'outil les RÉINITIALISE.
/// </summary>
public static class FittingVolumeProfileGenerator {
    private const string ProfilePath   = "Assets/Settings/Fitting Volume Profile.asset";
    private const string HudPrefabPath = "Assets/Resources/Prefabs/Managers/HUD Manager.prefab";

    [MenuItem("Tools/The BROZ/APS2/Generate Fitting Volume Profile")]
    public static void Generate() {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        bool created = profile == null;
        if (created) {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // DoF Bokeh « tout flou » : focus quasi au nez de la caméra, ouverture max, longue focale →
        // le cercle de confusion sature dès ~0,5 m ; le décor est uniformément flou (le sol aux
        // pieds du personnage comme le fond de la boutique).
        var dof = GetOrAdd<DepthOfField>(profile);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(0.3f);
        dof.aperture.Override(1f);
        dof.focalLength.Override(150f);
        dof.bladeCount.Override(5);

        // Pénombre : le personnage (non gradé) ressort du décor assombri et délavé.
        var color = GetOrAdd<ColorAdjustments>(profile);
        color.postExposure.Override(-0.8f);
        color.saturation.Override(-40f);

        var vignette = GetOrAdd<Vignette>(profile);
        vignette.color.Override(Color.black);
        vignette.intensity.Override(0.35f);
        vignette.smoothness.Override(0.5f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        bool assigned = AssignToHud(profile);
        Debug.Log($"[FittingVolumeProfileGenerator] {(created ? "created" : "updated")} {ProfilePath} ; FittingRoomUI (HUD Manager) {(assigned ? "assigned" : "NOT assigned")}.");
    }

    /// <summary>Override existant, ou créé comme SOUS-ASSET du profil (VolumeProfile.Add seul ne le
    /// persiste pas : la référence serait perdue à la sauvegarde).</summary>
    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent {
        if (!profile.TryGet(out T component)) {
            component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        component.active = true;
        EditorUtility.SetDirty(component);
        return component;
    }

    private static bool AssignToHud(VolumeProfile profile) {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try {
            FittingRoomUI ui = root.GetComponentInChildren<FittingRoomUI>(true);
            if (ui == null) {
                Debug.LogError($"[FittingVolumeProfileGenerator] No FittingRoomUI in {HudPrefabPath}.");
                return false;
            }
            var so = new SerializedObject(ui);
            so.FindProperty("fittingProfile").objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            return true;
        } finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
