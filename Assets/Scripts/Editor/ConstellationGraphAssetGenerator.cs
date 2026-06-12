#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Sim.Constellation;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Génère le graphe de constellation par défaut sous forme d'assets éditables :
///   - <c>Resources/Configurations/Constellation/ConstellationGraph.asset</c>
///     (le SO racine, avec la liste `branches` + `nodes` référencée)
///   - <c>Resources/Configurations/Constellation/Nodes/&lt;id&gt;.asset</c>
///     un asset <see cref="ConstellationNodeData"/> par nœud (~30 au total)
///
/// Une fois exécuté, tu peux ouvrir n'importe quel nœud individuellement dans
/// l'Inspector et l'éditer (cost, coût métier, prérequis, etc.) sans toucher
/// au code C#. Le runtime charge en priorité l'asset Resources via
/// <see cref="ConstellationGraphConfig.CreateDefault"/>'s fallback chain.
///
/// Idempotent : si les assets existent, on demande confirmation avant
/// d'écraser. Régénérer revient à effacer + recréer (tu perds les édits
/// manuels), donc à n'utiliser que pour un reset complet.
/// </summary>
internal static class ConstellationGraphAssetGenerator {

    private const string RootDir   = "Assets/Resources/Configurations/Constellation";
    private const string NodesDir  = RootDir + "/Nodes";
    private const string GraphPath = RootDir + "/ConstellationGraph.asset";

    [MenuItem("Tools/Constellation/Generate Default Graph Asset")]
    public static void Generate() {
        EnsureDir(RootDir);
        EnsureDir(NodesDir);

        var existing = AssetDatabase.LoadAssetAtPath<ConstellationGraphConfig>(GraphPath);
        if (existing != null) {
            bool overwrite = EditorUtility.DisplayDialog(
                "Constellation Graph",
                "Les assets existent déjà :\n\n" +
                GraphPath + "\n" + NodesDir + "/*.asset\n\n" +
                "Régénérer écrasera TOUT (les édits manuels seront perdus).",
                "Régénérer", "Annuler");
            if (!overwrite) return;
            DeleteExisting();
        }

        // 1) Construis le graphe + ses ~30 nœuds in-memory via la factory.
        var graph = ConstellationGraphConfig.CreateDefault();

        // 2) Sauve chaque nœud comme un asset distinct. SaveAssets + Refresh est
        //    appelé EN BOUCLE par sécurité — c'est lent mais ça garantit que la
        //    référence script (m_Script) est résolue et écrite avant la sauvegarde
        //    du graphe racine. Sans ça, les SO peuvent atterrir sur disque avec
        //    m_Script: {fileID: 0} → re-load null fields au runtime.
        var savedNodes = new List<ConstellationNodeData>(graph.nodes.Count);
        for (int i = 0; i < graph.nodes.Count; i++) {
            var node = graph.nodes[i];
            if (node == null || string.IsNullOrEmpty(node.id)) continue;
            string path = NodesDir + "/" + node.id + ".asset";
            AssetDatabase.CreateAsset(node, path);
            savedNodes.Add(node);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 3) Remplace la liste in-memory du graphe par les références aux
        //    assets qui viennent d'être écrits sur disque (rechargées via
        //    LoadAssetAtPath → instance « canonical » connue de l'AssetDatabase).
        graph.nodes.Clear();
        foreach (var n in savedNodes) {
            var reloaded = AssetDatabase.LoadAssetAtPath<ConstellationNodeData>(
                AssetDatabase.GetAssetPath(n));
            if (reloaded != null) graph.nodes.Add(reloaded);
        }

        // 4) Sauve le graphe racine.
        AssetDatabase.CreateAsset(graph, GraphPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 5) Vérification : on relit chaque asset et on s'assure que id est lisible.
        int brokenCount = 0;
        foreach (var n in savedNodes) {
            var reloaded = AssetDatabase.LoadAssetAtPath<ConstellationNodeData>(
                AssetDatabase.GetAssetPath(n));
            if (reloaded == null || string.IsNullOrEmpty(reloaded.id)) {
                brokenCount++;
                Debug.LogError("[ConstellationGraphAssetGenerator] Broken asset after save: " + AssetDatabase.GetAssetPath(n));
            }
        }

        Selection.activeObject = graph;
        EditorGUIUtility.PingObject(graph);
        if (brokenCount > 0) {
            Debug.LogError($"[ConstellationGraphAssetGenerator] {brokenCount} broken asset(s). Try regenerating after a full Unity recompile.");
        } else {
            Debug.Log("[ConstellationGraphAssetGenerator] Created " + GraphPath +
                      " + " + savedNodes.Count + " node assets under " + NodesDir);
        }
    }

    /// <summary>
    /// Supprime tout le dossier généré pour repartir d'un état propre. Utile si la
    /// précédente régénération est tombée sur un m_Script à 0 (recompile en cours).
    /// </summary>
    [MenuItem("Tools/Constellation/Delete Generated Assets")]
    public static void DeleteAll() {
        if (!EditorUtility.DisplayDialog(
            "Constellation Graph",
            "Supprimer définitivement :\n\n" + GraphPath + "\n" + NodesDir + "/*.asset\n\n" +
            "Le runtime retombera sur ConstellationGraphConfig.CreateDefault() (in-memory).",
            "Supprimer", "Annuler")) return;
        DeleteExisting();
        Debug.Log("[ConstellationGraphAssetGenerator] Deleted all generated assets.");
    }

    private static void DeleteExisting() {
        // Supprime le graphe ET les nœuds existants pour repartir propre.
        AssetDatabase.DeleteAsset(GraphPath);
        if (Directory.Exists(NodesDir)) {
            foreach (var guid in AssetDatabase.FindAssets("t:ConstellationNodeData", new[] { NodesDir })) {
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void EnsureDir(string path) {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }
}
#endif
