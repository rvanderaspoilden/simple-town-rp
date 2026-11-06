using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AdvancedPeopleSystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Génère un <see cref="ClothingConfig"/> GENRÉ par élément équipable APS2 (hat, accessory,
/// shirt, pants, shoes — version homme ET version femme) dans
/// Assets/Resources/Configurations/Items/Clothing/, avec l'icône du genre (studio d'icônes).
/// Idempotent : relancer met à jour les assets existants (GUID conservés).
///
/// IDs stables, PERSISTÉS EN BASE — ne jamais changer le schéma :
///   id = base de genre (HOMME 200, FEMME 300) + offset de catégorie (hat 0, accessory 20,
///   shirt 40, pants 60, shoes 80) + index du preset APS2 du genre.
/// Un vêtement ne s'équipe que sur un personnage de son genre (ClothingConfig.SupportsGender).
/// Le backpack APS2 (Item1) est exclu : l'item Sac à dos fonctionnel (id 109) existe déjà.
/// </summary>
public static class ClothingConfigGenerator {
    private const string MaleSettingsPath = "Assets/AdvancedPeopleSystem2/Settings/MaleSettings.asset";
    private const string FemaleSettingsPath = "Assets/AdvancedPeopleSystem2/Settings/FemaleSettings.asset";
    private const string OutputFolder = "Assets/Resources/Configurations/Items/Clothing";
    private const string IconFolder = "Assets/Resources/Sprites/Equipment/Generated";
    private const string PrefabFolder = "Assets/Resources/Prefabs/Items/Clothing";
    private const string FabricPrefabPath = PrefabFolder + "/clothes_folded.prefab";
    private const string PickActionPath = "Assets/Resources/Configurations/Actions/PICK.asset";
    private const string DropActionPath = "Assets/Resources/Configurations/Actions/DROP.asset";

    private struct Category {
        public CharacterElementType type;
        public string slug;
        public int idOffset;
        public bool fabric; // tissu : plié (Prop_Clothes_Folded) au sol AUSSI — pas de modèle 3D
    }

    private static readonly Category[] Categories = {
        new Category { type = CharacterElementType.Hat, slug = "hat", idOffset = 0 },
        new Category { type = CharacterElementType.Accessory, slug = "accessory", idOffset = 20 },
        new Category { type = CharacterElementType.Shirt, slug = "shirt", idOffset = 40, fabric = true },
        new Category { type = CharacterElementType.Pants, slug = "pants", idOffset = 60, fabric = true },
        new Category { type = CharacterElementType.Shoes, slug = "shoes", idOffset = 80 },
    };

    [MenuItem("Tools/The BROZ/APS2/Generate Clothing Item Configs")]
    public static void Generate() {
        var male = AssetDatabase.LoadAssetAtPath<CharacterSettings>(MaleSettingsPath);
        var female = AssetDatabase.LoadAssetAtPath<CharacterSettings>(FemaleSettingsPath);
        var pick = AssetDatabase.LoadAssetAtPath<Sim.Interactables.Action>(PickActionPath);
        var drop = AssetDatabase.LoadAssetAtPath<Sim.Interactables.Action>(DropActionPath);

        if (male == null || female == null || pick == null || drop == null) {
            Debug.LogError("[ClothingConfigGenerator] Missing dependency (settings or PICK/DROP actions).");
            return;
        }

        if (!WearablePrefabBuilder.TryGetFoldedMesh(out Mesh foldedMesh, out Material[] foldedMats)) return;

        Directory.CreateDirectory(OutputFolder);
        Directory.CreateDirectory(PrefabFolder);

        // Prefab PARTAGÉ des vêtements en tissu : plié en main ET au sol (pas de modèle 3D).
        ClothingItemBehaviour fabricBehaviour = WearablePrefabBuilder.Build<ClothingItemBehaviour>(
            FabricPrefabPath, "clothes_folded", null, null, foldedMesh, foldedMats);

        int created = 0, updated = 0;
        GenerateForGender(Gender.MALE, "male", male, 200, fabricBehaviour, foldedMesh, foldedMats, pick, drop, ref created, ref updated);
        GenerateForGender(Gender.FEMALE, "female", female, 300, fabricBehaviour, foldedMesh, foldedMats, pick, drop, ref created, ref updated);

        AssetDatabase.SaveAssets();
        Debug.Log($"[ClothingConfigGenerator] {created} created, {updated} updated in {OutputFolder}");
    }

    private static void GenerateForGender(Gender gender, string genderSlug, CharacterSettings settings,
        int genderIdBase, ClothingItemBehaviour fabricBehaviour, Mesh foldedMesh, Material[] foldedMats,
        Sim.Interactables.Action pick, Sim.Interactables.Action drop,
        ref int created, ref int updated)
    {
        foreach (var category in Categories) {
            List<CharacterElementsPreset> presets = settings.GetCharacterElementsPresets(category.type);
            if (presets == null) continue;

            for (int i = 0; i < presets.Count; i++) {
                if (!HasMesh(presets, i)) continue;

                // Prefab monde : tissu → pile pliée partagée ; sinon prefab dédié dont le visuel
                // SOL est le vrai modèle 3D (mesh LOD0 du preset) et le visuel MAIN la pile pliée.
                ItemBehaviour worldBehaviour = fabricBehaviour;
                if (!category.fabric) {
                    var preset = presets[i];
                    var mats = preset.mats != null && preset.mats.Length > 0 ? preset.mats : new[] { settings.bodyMaterial };
                    worldBehaviour = WearablePrefabBuilder.Build<ClothingItemBehaviour>(
                        $"{PrefabFolder}/{genderSlug}_{category.slug}_{i:00}.prefab",
                        $"{genderSlug}_{category.slug}_{i:00}",
                        preset.mesh[0], mats, foldedMesh, foldedMats);
                }

                string assetPath = $"{OutputFolder}/{genderSlug}_{category.slug}_{i:00}.asset";
                var config = AssetDatabase.LoadAssetAtPath<ClothingConfig>(assetPath);
                bool isNew = config == null;
                if (isNew) {
                    config = ScriptableObject.CreateInstance<ClothingConfig>();
                    AssetDatabase.CreateAsset(config, assetPath);
                    created++;
                } else {
                    updated++;
                }

                Sprite icon = FindIcon(genderSlug, category.slug, i);
                string label = FrenchLabel(category.type, presets[i].name);
                string genderFrench = gender == Gender.FEMALE ? "femme" : "homme";

                var so = new SerializedObject(config);
                so.FindProperty("id").intValue = genderIdBase + category.idOffset + i;
                so.FindProperty("label").stringValue = label;
                so.FindProperty("description").stringValue = $"Vêtement {genderFrench} ({CategoryFrench(category.type)}).";
                so.FindProperty("type").enumValueIndex = (int)ItemType.CLOTHING;
                // Porté en main comme un colis : pile pliée tenue à deux mains, pose BOX
                // (le resolver ne mappe BOX sur le layer Two Hand que pour un item TWO_HAND).
                so.FindProperty("handleType").enumValueIndex = (int)ItemHandleType.TWO_HAND;
                so.FindProperty("poseShape").enumValueIndex = (int)Sim.CarryShape.BOX;
                so.FindProperty("prefab").objectReferenceValue = worldBehaviour;
                so.FindProperty("toPersist").boolValue = true;
                so.FindProperty("allowedInPocket").boolValue = true;
                so.FindProperty("icon").objectReferenceValue = icon;
                so.FindProperty("maxStackSize").intValue = 1;
                // Grip partagé : en main, le visuel est toujours la même pile pliée.
                so.FindProperty("gripPosition").vector3Value = WearablePrefabBuilder.HeldGripPosition;
                so.FindProperty("gripEuler").vector3Value = WearablePrefabBuilder.HeldGripEuler;

                SerializedProperty unequipped = so.FindProperty("unEquippedActions");
                unequipped.arraySize = 1;
                unequipped.GetArrayElementAtIndex(0).objectReferenceValue = pick;

                SerializedProperty equipped = so.FindProperty("equippedActions");
                equipped.arraySize = 1;
                equipped.GetArrayElementAtIndex(0).objectReferenceValue = drop;

                so.FindProperty("elementType").enumValueIndex = (int)category.type;
                so.FindProperty("elementIndex").intValue = i;
                so.FindProperty("gender").enumValueIndex = (int)(gender == Gender.FEMALE ? Gender.FEMALE : Gender.MALE);
                so.ApplyModifiedPropertiesWithoutUndo();

                config.name = Path.GetFileNameWithoutExtension(assetPath);
                EditorUtility.SetDirty(config);
            }
        }
    }

    private static bool HasMesh(List<CharacterElementsPreset> presets, int index) {
        return presets != null && index < presets.Count
               && presets[index] != null
               && presets[index].mesh != null
               && presets[index].mesh.Length > 0
               && presets[index].mesh[0] != null;
    }

    /// <summary>Icône générée par EquipmentIconGenerator : {gender}_{slug}_{index:00}_{nom}.png.</summary>
    private static Sprite FindIcon(string gender, string slug, int index) {
        if (!Directory.Exists(IconFolder)) return null;
        string[] files = Directory.GetFiles(IconFolder, $"{gender}_{slug}_{index:00}_*.png");
        return files.Length > 0 ? AssetDatabase.LoadAssetAtPath<Sprite>(files[0].Replace('\\', '/')) : null;
    }

    private static string CategoryFrench(CharacterElementType type) {
        switch (type) {
            case CharacterElementType.Hat: return "chapeau";
            case CharacterElementType.Accessory: return "accessoire";
            case CharacterElementType.Shirt: return "haut";
            case CharacterElementType.Pants: return "pantalon";
            case CharacterElementType.Shoes: return "chaussures";
            default: return "vêtement";
        }
    }

    /// <summary>Label français depuis le nom de preset APS2 (« Shirt Type 3 », « Police Hat 1 »…).
    /// Même label pour les deux genres — le genre est porté par la description, la tooltip et
    /// l'icône (variante homme/femme).</summary>
    private static string FrenchLabel(CharacterElementType type, string presetName) {
        Match match = Regex.Match(presetName, @"(\d+)\s*$");
        string n = match.Success ? match.Groups[1].Value : string.Empty;
        bool police = presetName.Contains("Police");
        bool firefighter = presetName.Contains("Firefighter");
        bool doctor = presetName.Contains("Doctor");
        bool worker = presetName.Contains("Worker");
        bool pack = presetName.Contains("Pack");

        switch (type) {
            case CharacterElementType.Hat:
                if (police) return $"Casquette de police {n}".TrimEnd();
                if (firefighter) return "Casque de pompier";
                if (worker) return "Casque de chantier";
                if (pack) return $"Chapeau bonus {n}".TrimEnd();
                return $"Chapeau {n}".TrimEnd();
            case CharacterElementType.Accessory:
                return $"Accessoire {n}".TrimEnd();
            case CharacterElementType.Shirt:
                if (police) return $"Chemise de police {n}".TrimEnd();
                if (firefighter) return "Veste de pompier";
                if (doctor) return "Blouse de médecin";
                if (worker) return "Gilet de chantier";
                return $"Haut {n}".TrimEnd();
            case CharacterElementType.Pants:
                if (police) return $"Pantalon de police {n}".TrimEnd();
                if (firefighter) return "Pantalon de pompier";
                if (doctor) return "Pantalon de médecin";
                if (worker) return "Pantalon de chantier";
                return $"Pantalon {n}".TrimEnd();
            case CharacterElementType.Shoes:
                if (police) return $"Chaussures de police {n}".TrimEnd();
                if (firefighter) return "Bottes de pompier";
                if (doctor) return "Chaussures de médecin";
                if (worker) return "Chaussures de chantier";
                return $"Chaussures {n}".TrimEnd();
            default:
                return presetName;
        }
    }
}
