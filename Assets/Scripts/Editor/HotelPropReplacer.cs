#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Outil éditeur : remplace les "old" props (GameObjects cassés, souvent avec un script
/// manquant) sous le GameObject "Hotel" de la scène active par leur VRAI prefab.
///
/// Pour chaque enfant direct de "Hotel" qui n'est PAS déjà une instance de prefab :
///   - on retire un éventuel suffixe de copie " (N)" du nom,
///   - on cherche un prefab du même nom (normalisé) dans Resources/Prefabs/Props,
///   - si trouvé : on instancie le prefab à la même position/rotation/échelle/ordre,
///     on lui redonne le nom d'origine, puis on supprime l'ancien GameObject.
///   - si non trouvé : on laisse l'objet intact (géométrie du bâtiment, etc.).
///
/// Undo géré (Ctrl+Z), la scène est marquée dirty mais PAS sauvegardée : vérifier le
/// résultat puis sauvegarder manuellement. Relancer est idempotent (les instances déjà
/// remplacées sont ignorées).
///
/// Menu : Tools ▸ Hotel ▸ Replace Old Props With Prefabs
/// </summary>
public static class HotelPropReplacer
{
    private const string HotelName = "Hotel";
    private const string PrefabScope = "Assets/Resources/Prefabs/Props";

    [MenuItem("Tools/Hotel/Replace Old Props With Prefabs")]
    public static void Replace()
    {
        var hotel = GameObject.Find(HotelName);
        if (hotel == null)
        {
            Debug.LogError($"[HotelPropReplacer] '{HotelName}' introuvable dans la scène active.");
            return;
        }

        // Index des prefabs sources par nom normalisé.
        var prefabByName = new Dictionary<string, GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabScope }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string key = Norm(System.IO.Path.GetFileNameWithoutExtension(path));
            if (!prefabByName.ContainsKey(key))
                prefabByName[key] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // Snapshot des enfants directs (la hiérarchie change pendant la boucle).
        var children = new List<Transform>();
        foreach (Transform c in hotel.transform) children.Add(c);

        int replaced = 0, skipped = 0, alreadyPrefab = 0;
        var repLog = new StringBuilder();
        var skipLog = new StringBuilder();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Replace Hotel Old Props");
        int group = Undo.GetCurrentGroup();

        foreach (Transform old in children)
        {
            if (old == null) continue;

            // On ne touche pas aux instances de prefab déjà en place (déjà correctes).
            if (PrefabUtility.IsAnyPrefabInstanceRoot(old.gameObject)) { alreadyPrefab++; continue; }

            string baseName = StripCopySuffix(old.name);
            if (!prefabByName.TryGetValue(Norm(baseName), out var prefab) || prefab == null)
            {
                skipped++;
                skipLog.AppendLine(old.name);
                continue;
            }

            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hotel.transform);
                Undo.RegisterCreatedObjectUndo(inst, "Instantiate prop prefab");

                Transform it = inst.transform;
                it.SetPositionAndRotation(old.position, old.rotation);
                it.localScale = old.localScale;
                it.SetSiblingIndex(old.GetSiblingIndex());
                inst.name = old.name; // conserve le nom d'origine (avec son suffixe de copie)

                Undo.DestroyObjectImmediate(old.gameObject);
                replaced++;
                repLog.AppendLine($"{inst.name} → {prefab.name}");
            }
            catch (System.Exception e)
            {
                skipped++;
                skipLog.AppendLine($"{old.name} (ERREUR: {e.Message})");
            }
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(hotel.scene);

        Debug.Log($"[HotelPropReplacer] replaced={replaced} skipped(noMatch)={skipped} alreadyPrefab(ignored)={alreadyPrefab}");
        Debug.Log("[HotelPropReplacer] REMPLACÉS:\n" + repLog);
        Debug.Log("[HotelPropReplacer] NON REMPLACÉS (aucun prefab correspondant — géométrie/à voir):\n" + skipLog);
        Debug.Log("[HotelPropReplacer] Vérifie le résultat puis sauvegarde la scène (Ctrl+S). Ctrl+Z pour annuler.");
    }

    private static string StripCopySuffix(string name) =>
        Regex.Replace(name, @"\s*\(\d+\)\s*$", "").Trim();

    private static string Norm(string s) =>
        s.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
}
#endif
