using System.Collections.Generic;
using System.IO;
using System.Text;
using AdvancedPeopleSystem;
using Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders one transparent-background icon per equippable APS2 element (hat, accessory, shirt,
/// pants, shoes, backpack) for both genders, into Assets/Resources/Sprites/Equipment/Generated/.
/// Each garment is rendered isolated in bind pose (preset LOD0 mesh + preset materials) inside
/// the EquipmentIconStudio scene. These sprites are meant to back future clothing/equipment
/// ItemConfigs equippable from the inventory.
/// </summary>
public static class EquipmentIconGenerator {
    private const string ScenePath = "Assets/Scenes/EquipmentIconStudio.unity";
    private const string OutputFolder = "Assets/Resources/Sprites/Equipment/Generated";
    private const string MaleSettingsPath = "Assets/AdvancedPeopleSystem2/Settings/MaleSettings.asset";
    private const string FemaleSettingsPath = "Assets/AdvancedPeopleSystem2/Settings/FemaleSettings.asset";

    /// <summary>Element types that make sense as inventory equipment. Head/Hair/Beard are body
    /// customization (launcher), not items.</summary>
    private static readonly CharacterElementType[] EquippableTypes = {
        CharacterElementType.Hat,
        CharacterElementType.Accessory,
        CharacterElementType.Shirt,
        CharacterElementType.Pants,
        CharacterElementType.Shoes,
        CharacterElementType.Item1 // backpacks
    };

    [MenuItem("Tools/The BROZ/APS2/Build Equipment Icon Studio Scene")]
    public static void BuildStudioScene() {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var root = new GameObject("Icon Studio");
        var studio = root.AddComponent<EquipmentIconStudio>();

        var stage = new GameObject("Stage");
        stage.transform.SetParent(root.transform, false);
        studio.stage = stage.transform;

        var camGo = new GameObject("Render Camera");
        camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 50f;
        cam.allowHDR = false;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = false;
        camData.antialiasing = AntialiasingMode.None;
        studio.renderCamera = cam;

        var key = new GameObject("Key Light").AddComponent<Light>();
        key.transform.SetParent(root.transform, false);
        key.type = LightType.Directional;
        key.transform.rotation = Quaternion.Euler(40f, 230f, 0f);
        key.intensity = 1.1f;
        key.shadows = LightShadows.None;

        var fill = new GameObject("Fill Light").AddComponent<Light>();
        fill.transform.SetParent(root.transform, false);
        fill.type = LightType.Directional;
        fill.transform.rotation = Quaternion.Euler(15f, 60f, 0f);
        fill.intensity = 0.45f;
        fill.shadows = LightShadows.None;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f, 1f);
        RenderSettings.skybox = null;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[EquipmentIconGenerator] Studio scene saved to {ScenePath}");
    }

    [MenuItem("Tools/The BROZ/APS2/Generate Equipment Icons")]
    public static void GenerateAll() {
        var studio = EnsureStudioOpen();
        if (studio == null) {
            return;
        }

        var male = AssetDatabase.LoadAssetAtPath<CharacterSettings>(MaleSettingsPath);
        var female = AssetDatabase.LoadAssetAtPath<CharacterSettings>(FemaleSettingsPath);
        if (male == null || female == null) {
            Debug.LogError("[EquipmentIconGenerator] APS2 Male/Female settings assets not found.");
            return;
        }

        Directory.CreateDirectory(OutputFolder);

        var generatedPaths = new List<string>();
        int skipped = 0;

        try {
            Generate(studio, "male", male, generatedPaths, ref skipped);
            Generate(studio, "female", female, generatedPaths, ref skipped);
        } finally {
            EditorUtility.ClearProgressBar();
        }

        ImportAsSprites(generatedPaths);

        Debug.Log($"[EquipmentIconGenerator] {generatedPaths.Count} icons generated ({skipped} presets skipped) in {OutputFolder}");
    }

    private static EquipmentIconStudio EnsureStudioOpen() {
        if (!File.Exists(ScenePath)) {
            BuildStudioScene();
        } else if (EditorSceneManager.GetActiveScene().path != ScenePath) {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                return null;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        var studio = Object.FindFirstObjectByType<EquipmentIconStudio>();
        if (studio == null || studio.renderCamera == null || studio.stage == null) {
            Debug.LogError("[EquipmentIconGenerator] EquipmentIconStudio rig missing — run Build Equipment Icon Studio Scene.");
            return null;
        }

        return studio;
    }

    private static void Generate(EquipmentIconStudio studio, string gender, CharacterSettings settings, List<string> generatedPaths, ref int skipped) {
        foreach (var type in EquippableTypes) {
            var presets = settings.GetCharacterElementsPresets(type);
            if (presets == null) {
                continue;
            }

            for (int i = 0; i < presets.Count; i++) {
                var preset = presets[i];
                if (preset == null || preset.mesh == null || preset.mesh.Length == 0 || preset.mesh[0] == null) {
                    skipped++;
                    continue;
                }

                EditorUtility.DisplayProgressBar("Equipment icons", $"{gender}/{type} {i + 1}/{presets.Count} — {preset.name}", (float)i / presets.Count);

                string file = $"{gender}_{TypeSlug(type)}_{i:00}_{Slug(preset.name)}.png";
                string path = $"{OutputFolder}/{file}";
                RenderIcon(studio, settings, preset, path);
                generatedPaths.Add(path);
            }
        }
    }

    private static void RenderIcon(EquipmentIconStudio studio, CharacterSettings settings, CharacterElementsPreset preset, string path) {
        var subject = new GameObject("Icon Subject");
        subject.transform.SetParent(studio.stage, false);

        RenderTexture rt = null;
        Texture2D tex = null;

        try {
            var mesh = preset.mesh[0];
            subject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = subject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = BuildMaterials(settings, preset, mesh.subMeshCount);

            var cam = studio.renderCamera;
            var bounds = renderer.bounds;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            float distance = radius * studio.padding / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cam.transform.rotation = Quaternion.Euler(studio.viewEuler);
            cam.transform.position = bounds.center - cam.transform.forward * distance;

            rt = new RenderTexture(studio.resolution, studio.resolution, 24, RenderTextureFormat.ARGB32) {
                antiAliasing = 4
            };

            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request)) {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);
            } else {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex = new Texture2D(studio.resolution, studio.resolution, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, studio.resolution, studio.resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, tex.EncodeToPNG());
        } finally {
            if (rt != null) {
                Object.DestroyImmediate(rt);
            }

            if (tex != null) {
                Object.DestroyImmediate(tex);
            }

            Object.DestroyImmediate(subject);
        }
    }

    /// <summary>Preset materials padded to the submesh count (APS2 applies the same mats array to
    /// every LOD renderer); presets without mats fall back to the gender's body material.</summary>
    private static Material[] BuildMaterials(CharacterSettings settings, CharacterElementsPreset preset, int subMeshCount) {
        var source = preset.mats != null && preset.mats.Length > 0 ? preset.mats : new[] { settings.bodyMaterial };
        var mats = new Material[subMeshCount];
        for (int i = 0; i < subMeshCount; i++) {
            mats[i] = source[Mathf.Min(i, source.Length - 1)];
        }

        return mats;
    }

    private static void ImportAsSprites(List<string> paths) {
        AssetDatabase.Refresh();

        try {
            AssetDatabase.StartAssetEditing();

            foreach (var path in paths) {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        } finally {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
    }

    private static string TypeSlug(CharacterElementType type) {
        return type == CharacterElementType.Item1 ? "backpack" : type.ToString().ToLowerInvariant();
    }

    private static string Slug(string name) {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name.ToLowerInvariant()) {
            if (char.IsLetterOrDigit(c)) {
                sb.Append(c);
            } else if (sb.Length > 0 && sb[sb.Length - 1] != '_') {
                sb.Append('_');
            }
        }

        return sb.ToString().Trim('_');
    }
}
