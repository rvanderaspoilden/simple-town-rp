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
                string label = CivilLabel(genderSlug, category.slug, i) ?? FrenchLabel(category.type, presets[i].name);
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

    /// <summary>Libellés PARLANTS des articles civils, relevés sur les icônes générées
    /// (Sprites/Equipment/Generated). Clé « {genre}_{catégorie}_{index} » : les meshes civils
    /// DIFFÈRENT entre homme et femme (contrairement aux tenues de métier), d'où une table par
    /// genre. Les tenues de métier et tout index absent retombent sur <see cref="FrenchLabel"/>.</summary>
    private static readonly Dictionary<string, string> CivilLabels = new Dictionary<string, string> {
        // ── Chapeaux ──
        { "female_hat_00", "Bob rayé" },            { "female_hat_01", "Bonnet gris" },
        { "female_hat_02", "Bonnet à revers" },     { "female_hat_07", "Chapeau de paille" },
        { "female_hat_08", "Casquette blanche et bleue" }, { "female_hat_09", "Casquette bleu marine" },
        { "female_hat_10", "Casquette kaki" },      { "female_hat_11", "Chapka grise" },
        { "male_hat_00", "Casquette rouge" },       { "male_hat_01", "Bonnet à liseré rouge" },
        { "male_hat_02", "Bonnet gris" },           { "male_hat_07", "Chapeau de paille" },
        { "male_hat_08", "Casquette blanche et bleue" },   { "male_hat_09", "Casquette bleu marine" },
        { "male_hat_10", "Casquette kaki" },        { "male_hat_11", "Chapka verte" },
        // ── Accessoires ──
        { "female_accessory_00", "Lunettes rondes violettes" }, { "female_accessory_01", "Lunettes œil-de-chat" },
        { "female_accessory_02", "Lunettes de soleil bleues" }, { "female_accessory_03", "Casque audio bleu" },
        { "female_accessory_04", "Casque audio rouge" },        { "female_accessory_05", "Casque VR" },
        { "female_accessory_06", "Bandeau" },                   { "female_accessory_07", "Oreillette gauche" },
        { "female_accessory_08", "Oreillette droite" },         { "female_accessory_09", "Lunettes de sport" },
        { "female_accessory_10", "Masque chirurgical" },        { "female_accessory_11", "Lunettes rondes bleues" },
        { "female_accessory_12", "Lunettes orange" },
        { "male_accessory_00", "Lunettes de soleil noires" },   { "male_accessory_01", "Lunettes rondes grises" },
        { "male_accessory_02", "Lunettes de soleil carrées" },  { "male_accessory_03", "Casque audio bleu" },
        { "male_accessory_04", "Casque audio rouge" },          { "male_accessory_05", "Casque VR" },
        { "male_accessory_06", "Bandeau" },                     { "male_accessory_07", "Oreillette gauche" },
        { "male_accessory_08", "Oreillette droite" },           { "male_accessory_09", "Lunettes de sport" },
        { "male_accessory_10", "Masque chirurgical" },          { "male_accessory_11", "Lunettes rondes bleues" },
        { "male_accessory_12", "Lunettes orange" },
        // ── Hauts ──
        { "female_shirt_00", "T-shirt blanc" },     { "female_shirt_01", "Haut rouge et blanc" },
        { "female_shirt_02", "Pull rose" },         { "female_shirt_03", "Top bordeaux" },
        { "female_shirt_04", "T-shirt noir" },      { "female_shirt_05", "Robe bleue" },
        { "female_shirt_06", "Pull rose poudré" },  { "female_shirt_07", "Gilet gris" },
        { "female_shirt_08", "Débardeur vert" },    { "female_shirt_09", "Gilet rouge" },
        { "female_shirt_15", "Veste saumon" },
        { "male_shirt_00", "T-shirt bleu" },        { "male_shirt_01", "Débardeur blanc" },
        { "male_shirt_02", "Sweat à capuche jaune" }, { "male_shirt_03", "T-shirt raglan" },
        { "male_shirt_04", "Veste de costume" },    { "male_shirt_05", "Pull marron" },
        { "male_shirt_06", "Pull de Noël" },        { "male_shirt_07", "Chemise blanche" },
        { "male_shirt_08", "Veste de sport" },      { "male_shirt_09", "T-shirt gris" },
        { "male_shirt_15", "Veste saumon" },
        // ── Pantalons ──
        { "female_pants_00", "Short blanc" },       { "female_pants_01", "Pantalon rouge" },
        { "female_pants_02", "Pantalon rose" },     { "female_pants_03", "Pantalon bordeaux" },
        { "female_pants_04", "Short de sport noir" }, { "female_pants_05", "Pantalon bleu marine" },
        { "female_pants_06", "Pantalon gris" },     { "female_pants_07", "Short bleu" },
        { "female_pants_08", "Legging noir" },      { "female_pants_14", "Jean bleu" },
        { "male_pants_00", "Pantalon gris foncé" }, { "male_pants_01", "Short cargo blanc" },
        { "male_pants_02", "Pantalon jaune" },      { "male_pants_03", "Pantalon noir" },
        { "male_pants_04", "Pantalon anthracite" }, { "male_pants_05", "Pantalon marron" },
        { "male_pants_06", "Pantalon rouge" },      { "male_pants_07", "Jean délavé" },
        { "male_pants_08", "Pantalon vert" },       { "male_pants_09", "Pantalon beige" },
        { "male_pants_15", "Jean bleu" },
        // ── Chaussures ──
        { "female_shoes_00", "Baskets blanches" },  { "female_shoes_01", "Escarpins noirs" },
        { "female_shoes_02", "Baskets montantes blanches" }, { "female_shoes_03", "Escarpins rouges" },
        { "female_shoes_04", "Bottines noires" },   { "female_shoes_05", "Escarpins jaunes" },
        { "female_shoes_06", "Ballerines noires" }, { "female_shoes_07", "Escarpins gris" },
        { "female_shoes_08", "Baskets bleues" },    { "female_shoes_09", "Bottines blanches" },
        { "female_shoes_15", "Chaussures blanches" },
        { "male_shoes_00", "Baskets grises" },      { "male_shoes_01", "Sandales" },
        { "male_shoes_02", "Bottes de pluie jaunes" }, { "male_shoes_03", "Baskets blanches" },
        { "male_shoes_04", "Chaussures de ville noires" }, { "male_shoes_05", "Bottines marron" },
        { "male_shoes_06", "Bottines rouges" },     { "male_shoes_07", "Baskets bleues" },
        { "male_shoes_08", "Baskets vertes" },      { "male_shoes_09", "Richelieus noirs" },
        { "male_shoes_15", "Chaussures blanches" },
    };

    private static string CivilLabel(string genderSlug, string categorySlug, int index) {
        return CivilLabels.TryGetValue($"{genderSlug}_{categorySlug}_{index:00}", out string label) ? label : null;
    }

    /// <summary>Label français depuis le nom de preset APS2 (« Shirt Type 3 », « Police Hat 1 »…)
    /// — repli des tenues de métier et de tout article sans libellé dédié dans
    /// <see cref="CivilLabels"/>.</summary>
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
