using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Transforme les placeholders décoratifs du panneau équipement de l'inventaire (Equip Tête,
/// Accessoire, Torse, Jambes, Pieds — autorés dans HUD Manager.prefab) en vrais ItemSlot et
/// câble InventoryUI.wearableSlots dans l'ordre des slots de la place wearables
/// (ClothingConfig.WearSlotFor). Idempotent — relançable après retouche du prefab.
/// Les placeholders restants (Visage, Cou, Mains) restent décoratifs (catégories futures).
/// </summary>
public static class WearableSlotsBuilder {
    private const string HudPrefabPath = "Assets/Resources/Prefabs/Managers/HUD Manager.prefab";
    private const string GridPath = "Inventory Group/Player Inventory Panel/Equipment Panel/Equipment Grid";

    // Ordre = slots de la place wearables (0=chapeau, 1=accessoire, 2=haut, 3=pantalon, 4=chaussures).
    private static readonly string[] SlotNames = { "Equip Tête", "Equip Accessoire", "Equip Torse", "Equip Jambes", "Equip Pieds" };

    [MenuItem("Tools/The BROZ/Build Wearable Slots (HUD)")]
    public static void Build() {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try {
            Transform grid = root.transform.Find(GridPath);
            var inventory = root.GetComponentInChildren<InventoryUI>(true);
            if (grid == null || inventory == null) {
                Debug.LogError($"[WearableSlotsBuilder] Grid ou InventoryUI introuvable dans {HudPrefabPath}");
                return;
            }

            // Référence de style : le Backpack Slot (ItemSlot fonctionnel déjà autoré).
            Transform backpack = grid.Find("Backpack Slot");
            ItemSlot backpackSlot = backpack != null ? backpack.GetComponent<ItemSlot>() : null;
            Transform backpackHighlight = backpack != null ? backpack.Find("Drop Highlight") : null;

            var slots = new List<ItemSlot>();
            foreach (string name in SlotNames) {
                Transform t = grid.Find(name);
                if (t == null) {
                    Debug.LogError($"[WearableSlotsBuilder] Placeholder '{name}' introuvable — liste non câblée.");
                    return;
                }

                ItemSlot slot = t.GetComponent<ItemSlot>();
                if (slot == null) slot = t.gameObject.AddComponent<ItemSlot>();

                // Highlight de survol/drop : clone celui du Backpack Slot (même visuel).
                Transform highlight = t.Find("Drop Highlight");
                if (highlight == null && backpackHighlight != null) {
                    highlight = Object.Instantiate(backpackHighlight.gameObject, t).transform;
                    highlight.name = "Drop Highlight";
                    highlight.SetAsFirstSibling();
                }

                var so = new SerializedObject(slot);
                so.FindProperty("dropHighlight").objectReferenceValue = highlight != null ? highlight.GetComponent<Graphic>() : null;
                so.FindProperty("selectionOutline").objectReferenceValue = t.GetComponent<Outline>();
                so.FindProperty("rejectsNonPocketable").boolValue = false;
                if (backpackSlot != null) {
                    var backpackSo = new SerializedObject(backpackSlot);
                    so.FindProperty("highlightAlpha").floatValue = backpackSo.FindProperty("highlightAlpha").floatValue;
                    so.FindProperty("highlightFadeDuration").floatValue = backpackSo.FindProperty("highlightFadeDuration").floatValue;
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                slots.Add(slot);
            }

            var invSo = new SerializedObject(inventory);
            SerializedProperty listProp = invSo.FindProperty("wearableSlots");
            listProp.arraySize = slots.Count;
            for (int i = 0; i < slots.Count; i++) {
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }
            invSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            Debug.Log($"[WearableSlotsBuilder] {slots.Count} slots vêtements câblés dans {HudPrefabPath}");
        } finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
