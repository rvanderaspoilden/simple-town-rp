using System.Collections.Generic;
using System.IO;
using Sim.Scriptables;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Génère le catalogue par défaut de la boutique de vêtements
/// (<see cref="ClothingShopCatalogConfig"/>, Resources/Configurations/Shops/ClothingShopCatalog) :
/// tous les <see cref="ClothingConfig"/> CIVILS des deux genres (les tenues de métier —
/// police/pompier/chantier/médecin — s'obtiennent en jeu, pas en boutique), prix par catégorie.
/// Idempotent : relancer remplace les entrées (GUID de l'asset conservé).
/// </summary>
public static class ClothingShopCatalogGenerator {
    private const string ConfigFolder = "Assets/Resources/Configurations/Shops";
    private const string AssetPath = ConfigFolder + "/ClothingShopCatalog.asset";
    private const string ClothingFolder = "Assets/Resources/Configurations/Items/Clothing";

    private static readonly string[] ProfessionMarkers = { "police", "pompier", "chantier", "médecin" };

    [MenuItem("Tools/The BROZ/APS2/Generate Clothing Shop Catalog")]
    public static void Generate() {
        Directory.CreateDirectory(ConfigFolder);

        var catalog = AssetDatabase.LoadAssetAtPath<ClothingShopCatalogConfig>(AssetPath);
        bool isNew = catalog == null;
        if (isNew) {
            catalog = ScriptableObject.CreateInstance<ClothingShopCatalogConfig>();
            AssetDatabase.CreateAsset(catalog, AssetPath);
        }

        var entries = new List<(ClothingConfig cfg, int price)>();
        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { ClothingFolder })) {
            var cfg = AssetDatabase.LoadAssetAtPath<ClothingConfig>(AssetDatabase.GUIDToAssetPath(guid));
            if (cfg == null || IsProfessionOutfit(cfg.Label)) continue;
            entries.Add((cfg, PriceFor(cfg)));
        }
        entries.Sort((a, b) => a.cfg.ID.CompareTo(b.cfg.ID));

        var so = new SerializedObject(catalog);
        so.FindProperty("shopLabel").stringValue = "Boutique de vêtements";
        SerializedProperty list = so.FindProperty("entries");
        list.arraySize = entries.Count;
        for (int i = 0; i < entries.Count; i++) {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("item").objectReferenceValue = entries[i].cfg;
            element.FindPropertyRelative("price").intValue = entries[i].price;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ClothingShopCatalogGenerator] {(isNew ? "Created" : "Updated")} {AssetPath} with {entries.Count} entries.");
    }

    private static bool IsProfessionOutfit(string label) {
        if (string.IsNullOrEmpty(label)) return false;
        string lower = label.ToLowerInvariant();
        foreach (string marker in ProfessionMarkers) {
            if (lower.Contains(marker)) return true;
        }
        return false;
    }

    private static int PriceFor(ClothingConfig cfg) {
        switch (cfg.WearSlotIndex) {
            case 0:  return 25; // chapeau
            case 1:  return 20; // accessoire
            case 2:  return 30; // haut
            case 3:  return 35; // pantalon
            case 4:  return 45; // chaussures
            default: return 30;
        }
    }
}
