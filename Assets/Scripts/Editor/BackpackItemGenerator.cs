using System.IO;
using AdvancedPeopleSystem;
using Sim.Scriptables;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Génère un <see cref="BackpackConfig"/> par SAC À DOS APS2 (presets Item1, meshes identiques
/// homme/femme). Géré comme un vêtement : le visuel PORTÉ est l'élément APS2 activé/désactivé
/// (rien d'instancié) ; le prefab monde (WearablePrefabBuilder) montre le VRAI modèle 3D au sol
/// et la pile de linge pliée en main ; l'icône vient du studio d'icônes (male_backpack_XX).
///
/// Idempotent : relancer met à jour les assets (GUID conservés). L'ancien « Sac à dos » id 109
/// est déplacé/converti en backpack_type_1 (id STABLE, persisté en base — ne pas changer).
/// Ids : 109 = type 1, 112-115 = types 2-5.
/// </summary>
public static class BackpackItemGenerator {
    private const string MaleSettingsPath = "Assets/AdvancedPeopleSystem2/Settings/MaleSettings.asset";
    private const string ConfigFolder = "Assets/Resources/Configurations/Items/Backpacks";
    private const string LegacyConfigPath = "Assets/Resources/Configurations/Items/Backpack.asset";
    private const string IconFolder = "Assets/Resources/Sprites/Equipment/Generated";
    private const string PrefabFolder = "Assets/Resources/Prefabs/Items/Backpacks";
    private const string PickActionPath = "Assets/Resources/Configurations/Actions/PICK.asset";
    private const string OpenActionPath = "Assets/Resources/Configurations/Actions/OPEN.asset";
    private const string DropActionPath = "Assets/Resources/Configurations/Actions/DROP.asset";

    private struct BackpackDef {
        public string key;          // nom de fichier config
        public int id;              // id STABLE persisté en DB
        public string label;
        public string description;
        public int slotCount;
        public int apsElementIndex; // preset APS2 Item1
    }

    private static readonly BackpackDef[] Defs = {
        new BackpackDef { key = "backpack_type_1", id = 109, label = "Sac à dos urbain", slotCount = 8, apsElementIndex = 0,
            description = "Un petit sac à dos citadin. Porté sur le dos, il ajoute 8 emplacements à l'inventaire." },
        new BackpackDef { key = "backpack_type_2", id = 112, label = "Sac à dos scolaire", slotCount = 10, apsElementIndex = 1,
            description = "Un sac à dos d'écolier bien pratique. Porté sur le dos, il ajoute 10 emplacements à l'inventaire." },
        new BackpackDef { key = "backpack_type_3", id = 113, label = "Sac à dos baroudeur", slotCount = 12, apsElementIndex = 2,
            description = "Un sac à dos souple en toile épaisse. Porté sur le dos, il ajoute 12 emplacements à l'inventaire." },
        new BackpackDef { key = "backpack_type_4", id = 114, label = "Sac de randonnée", slotCount = 16, apsElementIndex = 3,
            description = "Un grand sac de randonnée à armature. Porté sur le dos, il ajoute 16 emplacements à l'inventaire." },
        new BackpackDef { key = "backpack_type_5", id = 115, label = "Sac d'expédition", slotCount = 18, apsElementIndex = 4,
            description = "Un volumineux sac d'expédition avec matelas roulé. Porté sur le dos, il ajoute 18 emplacements à l'inventaire." },
    };

    [MenuItem("Tools/The BROZ/APS2/Generate Backpack Items")]
    public static void Generate() {
        var male = AssetDatabase.LoadAssetAtPath<CharacterSettings>(MaleSettingsPath);
        var pick = AssetDatabase.LoadAssetAtPath<Sim.Interactables.Action>(PickActionPath);
        var open = AssetDatabase.LoadAssetAtPath<Sim.Interactables.Action>(OpenActionPath);
        var drop = AssetDatabase.LoadAssetAtPath<Sim.Interactables.Action>(DropActionPath);
        if (male == null || pick == null || open == null || drop == null) {
            Debug.LogError("[BackpackItemGenerator] Missing dependency (MaleSettings or PICK/OPEN/DROP actions).");
            return;
        }

        if (!WearablePrefabBuilder.TryGetFoldedMesh(out Mesh foldedMesh, out Material[] foldedMats)) return;

        var presets = male.GetCharacterElementsPresets(CharacterElementType.Item1);

        Directory.CreateDirectory(ConfigFolder);
        Directory.CreateDirectory(PrefabFolder);

        int created = 0, updated = 0;
        foreach (var def in Defs) {
            var preset = presets != null && def.apsElementIndex < presets.Count ? presets[def.apsElementIndex] : null;
            if (preset == null || preset.mesh == null || preset.mesh.Length == 0 || preset.mesh[0] == null) {
                Debug.LogError($"[BackpackItemGenerator] APS2 Item1 preset {def.apsElementIndex} missing — {def.key} skipped.");
                continue;
            }

            // Prefab monde : vrai modèle du sac au sol, pile pliée en main (porté = élément APS2).
            var mats = preset.mats != null && preset.mats.Length > 0 ? preset.mats : new[] { male.bodyMaterial };
            BackpackItemBehaviour worldBehaviour = WearablePrefabBuilder.Build<BackpackItemBehaviour>(
                $"{PrefabFolder}/{def.key}.prefab", def.key, preset.mesh[0], mats, foldedMesh, foldedMats);

            WriteConfig(def, worldBehaviour, pick, open, drop, ref created, ref updated);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[BackpackItemGenerator] {created} created, {updated} updated ({Defs.Length} backpacks) in {ConfigFolder}");
    }

    private static void WriteConfig(BackpackDef def, BackpackItemBehaviour worldBehaviour, Sim.Interactables.Action pick,
        Sim.Interactables.Action open, Sim.Interactables.Action drop, ref int created, ref int updated)
    {
        string assetPath = $"{ConfigFolder}/{def.key}.asset";

        // L'ancien « Sac à dos » 109 est déplacé (GUID conservé — références scènes/loot intactes)
        // puis converti ItemContainerConfig → BackpackConfig via m_Script.
        if (def.id == 109 && AssetDatabase.LoadAssetAtPath<ItemConfig>(assetPath) == null
                          && AssetDatabase.LoadAssetAtPath<ItemConfig>(LegacyConfigPath) != null) {
            string error = AssetDatabase.MoveAsset(LegacyConfigPath, assetPath);
            if (!string.IsNullOrEmpty(error)) {
                Debug.LogError($"[BackpackItemGenerator] Move {LegacyConfigPath} failed: {error}");
                return;
            }
        }

        var config = AssetDatabase.LoadAssetAtPath<BackpackConfig>(assetPath);
        if (config == null) {
            var existing = AssetDatabase.LoadAssetAtPath<ItemConfig>(assetPath);
            if (existing != null) {
                ConvertScriptType(existing);
                config = AssetDatabase.LoadAssetAtPath<BackpackConfig>(assetPath);
                if (config == null) {
                    Debug.LogError($"[BackpackItemGenerator] {assetPath} script conversion to BackpackConfig failed.");
                    return;
                }
                updated++;
            } else {
                config = ScriptableObject.CreateInstance<BackpackConfig>();
                AssetDatabase.CreateAsset(config, assetPath);
                created++;
            }
        } else {
            updated++;
        }

        var so = new SerializedObject(config);
        so.FindProperty("id").intValue = def.id;
        so.FindProperty("label").stringValue = def.label;
        so.FindProperty("description").stringValue = def.description;
        so.FindProperty("type").enumValueIndex = (int)ItemType.PACKAGE;
        // Porté en main comme un colis : pile pliée tenue à deux mains, pose BOX.
        so.FindProperty("handleType").enumValueIndex = (int)ItemHandleType.TWO_HAND;
        so.FindProperty("poseShape").enumValueIndex = (int)Sim.CarryShape.BOX;
        so.FindProperty("prefab").objectReferenceValue = worldBehaviour;
        so.FindProperty("toPersist").boolValue = true;
        // Item classique : se ramasse dans l'inventaire (poche d'abord), une main, hotbar OK —
        // seuls les items de mission 2 mains monopolisent les mains.
        so.FindProperty("allowedInPocket").boolValue = true;
        so.FindProperty("icon").objectReferenceValue = FindIcon(def.apsElementIndex);
        so.FindProperty("maxStackSize").intValue = 1;
        // Grip partagé : en main, le visuel est toujours la même pile pliée.
        so.FindProperty("gripPosition").vector3Value = WearablePrefabBuilder.HeldGripPosition;
        so.FindProperty("gripEuler").vector3Value = WearablePrefabBuilder.HeldGripEuler;

        SerializedProperty unequipped = so.FindProperty("unEquippedActions");
        unequipped.arraySize = 2;
        unequipped.GetArrayElementAtIndex(0).objectReferenceValue = pick;
        unequipped.GetArrayElementAtIndex(1).objectReferenceValue = open;

        SerializedProperty equipped = so.FindProperty("equippedActions");
        equipped.arraySize = 1;
        equipped.GetArrayElementAtIndex(0).objectReferenceValue = drop;

        so.FindProperty("container.slotCount").intValue = def.slotCount;
        so.FindProperty("container.wearable").boolValue = true;
        so.FindProperty("container.acceptsProps").boolValue = false;
        so.FindProperty("container.allowsNestedContainers").boolValue = false;

        so.FindProperty("apsElementIndex").intValue = def.apsElementIndex;
        so.ApplyModifiedPropertiesWithoutUndo();

        config.name = Path.GetFileNameWithoutExtension(assetPath);
        EditorUtility.SetDirty(config);
    }

    /// <summary>Change le script d'un asset existant (ItemContainerConfig → BackpackConfig) : les
    /// champs sérialisés sont conservés par nom, le GUID de l'asset ne bouge pas.</summary>
    private static void ConvertScriptType(ItemConfig existing) {
        var temp = ScriptableObject.CreateInstance<BackpackConfig>();
        MonoScript target = MonoScript.FromScriptableObject(temp);
        Object.DestroyImmediate(temp);

        var so = new SerializedObject(existing);
        so.FindProperty("m_Script").objectReferenceValue = target;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(existing));
    }

    /// <summary>Icône du studio d'icônes (male_backpack_{index:00}_* — sacs identiques pour les
    /// deux genres, la variante male fait foi).</summary>
    private static Sprite FindIcon(int apsElementIndex) {
        if (!Directory.Exists(IconFolder)) return null;
        string[] files = Directory.GetFiles(IconFolder, $"male_backpack_{apsElementIndex:00}_*.png");
        return files.Length > 0 ? AssetDatabase.LoadAssetAtPath<Sprite>(files[0].Replace('\\', '/')) : null;
    }
}
