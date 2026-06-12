#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Sim.Constellation.Branches;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Crée / met à jour les assets <see cref="BranchConfig"/> sous
/// <c>Resources/Configurations/Constellation/Branches/</c> : les 4 branches racines + la
/// sous-branche métier « Livreur ».
///
/// UPSERT (et non delete+recreate) : on charge l'asset existant et on met à jour ses
/// champs en place. CRITIQUE — les <c>ConstellationNodeData.branch</c> / <c>definesBranch</c>
/// référencent ces assets par GUID ; les supprimer/recréer changerait le GUID et casserait
/// toutes les références. On ne touche jamais au .meta.
///
/// Seed le champ <c>id</c> (clé de devise unifiée) et la hiérarchie <c>parent</c>.
/// </summary>
internal static class BranchAssetGenerator {

    private const string TargetDir = "Assets/Resources/Configurations/Constellation/Branches";

    [MenuItem("Tools/Constellation/Generate Branch Assets")]
    public static void Generate() {
        EnsureDir(TargetDir);

        // 4 racines. id = nom de fichier (= ancienne clé enum.ToString() → JSONB intacte).
        var roots = new List<Spec> {
            new Spec("Creatif",  "Creatif",  "Créatif",
                     new Color(1.00f, 0.72f, 0.42f), true,
                     "Tronc créatif : art, musique, cuisine, décoration."),
            new Spec("Ingenieux", "Ingenieux", "Ingénieux",
                     new Color(0.55f, 0.78f, 0.98f), false,
                     "Tronc Métier : devise interne non affichée. Les sous-métiers portent leurs devises."),
            new Spec("Sportif",  "Sportif",  "Sportif",
                     new Color(0.62f, 0.86f, 0.58f), true,
                     "Tronc sportif : livraisons, course, vélo."),
            new Spec("Sociable", "Sociable", "Sociable",
                     new Color(0.99f, 0.86f, 0.45f), true,
                     "Tronc social : rencontres, groupes, événements."),
        };

        var byId = new Dictionary<string, BranchConfig>();
        foreach (var s in roots) byId[s.id] = Upsert(s, parent: null);

        // Sous-branche métier Livreur (parent = Ingénieux). id = devise existante
        // "delivery_driver" → clé JSONB inchangée, rewards de mission compatibles.
        byId.TryGetValue("Ingenieux", out var engineeringBranch);
        var deliveryDriver = Upsert(new Spec("DeliveryDriver", "delivery_driver", "Livreur",
                                             new Color(0.40f, 0.72f, 0.90f), true,
                                             "Sous-branche métier sous Ingénieux. Devise dépensée par l'arbre Livreur."),
                                    parent: engineeringBranch);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BranchDatabase.Reload();

        Selection.activeObject = deliveryDriver;
        Debug.Log($"[BranchAssetGenerator] Upserted {roots.Count} root branches + DeliveryDriver sub-branch under {TargetDir}.");
    }

    private static BranchConfig Upsert(Spec s, BranchConfig parent) {
        string path = $"{TargetDir}/{s.fileName}.asset";
        var b = AssetDatabase.LoadAssetAtPath<BranchConfig>(path);
        bool created = false;
        if (b == null) {
            b = ScriptableObject.CreateInstance<BranchConfig>();
            AssetDatabase.CreateAsset(b, path);
            created = true;
        }
        b.id = s.id;
        b.displayName = s.displayName;
        b.description = s.description;
        b.color = s.color;
        b.showInProfile = s.showInProfile;
        b.parent = parent;
        b.name = s.fileName;
        EditorUtility.SetDirty(b);
        Debug.Log($"[BranchAssetGenerator] {(created ? "Created" : "Updated")} {s.fileName} (id={s.id}, parent={(parent != null ? parent.id : "—")})");
        return b;
    }

    private static void EnsureDir(string path) {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }

    private struct Spec {
        public string fileName;
        public string id;
        public string displayName;
        public Color color;
        public bool showInProfile;
        public string description;
        public Spec(string fileName, string id, string displayName,
                    Color color, bool showInProfile, string description) {
            this.fileName = fileName;
            this.id = id;
            this.displayName = displayName;
            this.color = color;
            this.showInProfile = showInProfile;
            this.description = description;
        }
    }
}
#endif
