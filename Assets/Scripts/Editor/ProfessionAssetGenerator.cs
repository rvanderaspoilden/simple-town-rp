#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Sim.Professions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Génère les 7 assets <see cref="ProfessionConfig"/> par défaut sous
/// <c>Resources/Configurations/Professions/</c>.
///
/// Une fois exécuté, ces assets deviennent la source unique de vérité pour les métiers :
/// référencés par <c>MissionDefinition.profession</c>, les composants de scène carrière,
/// et la constellation. L'identité canonique est <c>id</c> (string).
///
/// Idempotent : si les assets existent, on demande confirmation avant d'écraser.
/// </summary>
internal static class ProfessionAssetGenerator {

    private const string TargetDir = "Assets/Resources/Configurations/Professions";

    [MenuItem("Tools/Profession/Generate Default Assets")]
    public static void Generate() {
        EnsureDir(TargetDir);

        // Si au moins un asset existe déjà, demande confirmation avant d'écraser.
        var existing = AssetDatabase.FindAssets("t:ProfessionConfig", new[] { TargetDir });
        if (existing.Length > 0) {
            bool overwrite = EditorUtility.DisplayDialog(
                "Profession Assets",
                $"{existing.Length} asset(s) ProfessionConfig existent déjà sous :\n\n{TargetDir}\n\n" +
                "Régénérer écrasera TOUS ces assets (les édits manuels seront perdus).",
                "Régénérer", "Annuler");
            if (!overwrite) return;
            foreach (var guid in existing) {
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Spec des 7 métiers (id stable + libellé FR + salaire de base).
        var specs = new List<Spec> {
            new Spec("Delivery",  "delivery_driver", "Livreur",
                    "Tournées en ville : tri, emballage, livraison. Métier de mouvement et de relations clients.",
                    baseSalary: 100),
            new Spec("Cleaning",  "cleaning",        "Agent d'entretien",
                    "Maintien la ville propre : ramasse les déchets et entretient les espaces communs.",
                    baseSalary: 80),
            new Spec("Repair",    "repair",          "Réparateur",
                    "Remet en état le mobilier urbain et les objets cassés.",
                    baseSalary: 120),
            new Spec("Gardening", "gardening",       "Jardinier",
                    "Entretient les espaces verts et plante de nouvelles essences.",
                    baseSalary: 90),
            new Spec("Concierge", "concierge",       "Concierge",
                    "Accueille les habitants, gère les clés et le courrier des immeubles.",
                    baseSalary: 100),
            new Spec("Music",     "music",           "Musicien",
                    "Anime les rues et les événements de la ville par sa musique.",
                    baseSalary: 80),
            new Spec("Custom",    "custom",          "Indépendant",
                    "Métier libre. Pas de salaire automatique.",
                    baseSalary: 0),
        };

        var created = new List<ProfessionConfig>();
        foreach (var s in specs) {
            var p = ScriptableObject.CreateInstance<ProfessionConfig>();
            p.id = s.id;
            p.displayName = s.displayName;
            p.description = s.description;
            p.baseSalary = s.baseSalary;
            p.name = s.fileName;
            string path = $"{TargetDir}/{s.fileName}.asset";
            AssetDatabase.CreateAsset(p, path);
            created.Add(p);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Vérifie qu'aucun asset n'a fini avec m_Script: {fileID: 0} — c'est le piège qu'on
        // a vécu lors du refactor ConstellationNodeData → SO.
        int broken = 0;
        foreach (var p in created) {
            var reloaded = AssetDatabase.LoadAssetAtPath<ProfessionConfig>(AssetDatabase.GetAssetPath(p));
            if (reloaded == null || string.IsNullOrEmpty(reloaded.id)) {
                broken++;
                Debug.LogError("[ProfessionAssetGenerator] Broken asset after save: " + AssetDatabase.GetAssetPath(p));
            }
        }

        ProfessionDatabase.Reload();
        Selection.activeObject = created.Count > 0 ? created[0] : null;
        if (broken > 0) {
            Debug.LogError($"[ProfessionAssetGenerator] {broken} broken asset(s). Try regenerating after a full Unity recompile.");
        } else {
            Debug.Log($"[ProfessionAssetGenerator] Created {created.Count} ProfessionConfig assets under {TargetDir}");
        }
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
        public string description;
        public int baseSalary;
        public Spec(string fileName, string id, string displayName,
                    string description, int baseSalary) {
            this.fileName = fileName;
            this.id = id;
            this.displayName = displayName;
            this.description = description;
            this.baseSalary = baseSalary;
        }
    }
}
#endif
