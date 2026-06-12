#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Sim.Missions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Génère les 5 assets <c>RewardKind.asset</c> par défaut, un par type concret de
/// récompense, sous <c>Resources/Configurations/Missions/RewardKinds/</c>.
///
/// Ces 5 assets remplacent les 7+ assets typés-par-amount actuels (Money_25, Money_100,
/// JobXpReward, etc.). Chaque mission référence un de ces 5 assets via une
/// <see cref="RewardEntry"/> avec son propre montant.
///
/// Idempotent : si les assets existent, demande confirmation avant d'écraser.
/// Vérifie post-save qu'aucun asset n'a un <c>m_Script: {fileID: 0}</c> (piège déjà
/// vécu sur ConstellationNodeData).
/// </summary>
internal static class RewardKindAssetGenerator {

    private const string TargetDir = "Assets/Resources/Configurations/Missions/RewardKinds";

    [MenuItem("Tools/Mission/Generate Reward Kind Assets")]
    public static void Generate() {
        EnsureDir(TargetDir);

        var existing = AssetDatabase.FindAssets("t:RewardKind", new[] { TargetDir });
        if (existing.Length > 0) {
            bool overwrite = EditorUtility.DisplayDialog(
                "Reward Kind Assets",
                $"{existing.Length} asset(s) RewardKind existent déjà sous :\n\n{TargetDir}\n\n" +
                "Régénérer écrasera tous ces assets (les paramètres de scoring du " +
                "ScoreModulatedMoneyReward seront perdus).",
                "Régénérer", "Annuler");
            if (!overwrite) return;
            foreach (var guid in existing) {
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Spec : un asset par sous-classe de RewardKind. Le nom de fichier est court
        // (sans suffixe « Kind ») pour être agréable dans le Project window — le Type
        // dans l'Inspector indique déjà la classe.
        // V3 : XpRewardKind + SocialCreditRewardKind retirés (XP supprimé end-to-end ;
        // social credit remplacé par BranchPointRewardKind ciblé sur la branche Sociable).
        // BranchPointRewardKind est généré séparément via le menu dédié ci-dessous, parce
        // que l'utilisateur doit dupliquer l'asset par branche (un par branche qu'il veut
        // créditer dans ses missions).
        var specs = new List<Spec> {
            new Spec("MoneyReward",                typeof(MoneyRewardKind)),
            new Spec("ScoreModulatedMoneyReward",  typeof(ScoreModulatedMoneyRewardKind)),
            new Spec("OwnProfessionPointReward",   typeof(OwnProfessionPointRewardKind)),
        };

        var created = new List<RewardKind>();
        foreach (var s in specs) {
            var inst = ScriptableObject.CreateInstance(s.type) as RewardKind;
            if (inst == null) {
                Debug.LogError($"[RewardKindAssetGenerator] CreateInstance failed for {s.type.Name}");
                continue;
            }
            inst.name = s.fileName;
            string path = $"{TargetDir}/{s.fileName}.asset";
            AssetDatabase.CreateAsset(inst, path);
            created.Add(inst);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Garde-fou : si un asset est sauvé avec m_Script: 0, le runtime le verra comme
        // un placeholder vide et les rewards ne fireront pas.
        int broken = 0;
        foreach (var k in created) {
            var reloaded = AssetDatabase.LoadAssetAtPath<RewardKind>(AssetDatabase.GetAssetPath(k));
            if (reloaded == null) {
                broken++;
                Debug.LogError("[RewardKindAssetGenerator] Broken asset: " + AssetDatabase.GetAssetPath(k));
            }
        }

        Selection.activeObject = created.Count > 0 ? created[0] : null;
        if (broken > 0) {
            Debug.LogError($"[RewardKindAssetGenerator] {broken} broken asset(s). Retry after a full Unity recompile.");
        } else {
            Debug.Log($"[RewardKindAssetGenerator] Created {created.Count} RewardKind assets under {TargetDir}");
        }
    }

    /// <summary>
    /// Crée un asset BranchPointReward neutre (champ branch à null). L'utilisateur le
    /// duplique ensuite par branche dont il a besoin (Ctrl+D → renomme « BranchPointReward_Sociable »
    /// → assigne branch = Sociable.asset dans l'Inspector). 4 duplications max (1 par
    /// branche), zéro génération automatique parce que le « cible » est un choix d'authoring.
    /// </summary>
    [MenuItem("Tools/Mission/Generate Branch Reward Asset")]
    public static void GenerateBranchReward() {
        EnsureDir(TargetDir);
        string path = $"{TargetDir}/BranchPointReward.asset";
        var existing = AssetDatabase.LoadAssetAtPath<BranchPointRewardKind>(path);
        if (existing != null) {
            bool overwrite = EditorUtility.DisplayDialog(
                "Branch Reward Asset",
                $"{path} existe déjà. Écraser ?\n\n" +
                "Note : tu veux probablement DUPLIQUER cet asset (Ctrl+D) une fois par branche " +
                "que tu veux créditer (ex. BranchPointReward_Sociable, BranchPointReward_Creatif).",
                "Écraser", "Annuler");
            if (!overwrite) return;
            AssetDatabase.DeleteAsset(path);
        }
        var inst = ScriptableObject.CreateInstance<BranchPointRewardKind>();
        inst.name = "BranchPointReward";
        AssetDatabase.CreateAsset(inst, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = inst;
        EditorGUIUtility.PingObject(inst);
        Debug.Log($"[RewardKindAssetGenerator] Created neutral BranchPointReward at {path}. " +
                  "Duplique cet asset (Ctrl+D) et assigne `branch` par variant.");
    }

    /// <summary>
    /// Crée un asset ProfessionPointReward neutre (champ profession à null). Même
    /// patron que BranchPointReward — l'utilisateur le duplique (Ctrl+D) une fois par
    /// profession qu'il veut pouvoir créditer EXPLICITEMENT depuis une mission (ex.
    /// ProfessionPointReward_Livreur, ProfessionPointReward_Reparateur). Sert aux cas où
    /// la mission veut donner des points à un métier DIFFÉRENT du sien — pour le cas
    /// courant (la mission crédite SA profession) il faut utiliser
    /// <c>OwnProfessionPointReward.asset</c>, qui lit automatiquement
    /// <c>mission.Definition.Profession</c>.
    /// </summary>
    [MenuItem("Tools/Mission/Generate Profession Reward Asset (Explicit)")]
    public static void GenerateProfessionReward() {
        EnsureDir(TargetDir);
        string path = $"{TargetDir}/ProfessionPointReward.asset";
        var existing = AssetDatabase.LoadAssetAtPath<ProfessionPointRewardKind>(path);
        if (existing != null) {
            bool overwrite = EditorUtility.DisplayDialog(
                "Profession Reward Asset (Explicit)",
                $"{path} existe déjà. Écraser ?\n\n" +
                "Note : tu veux probablement DUPLIQUER cet asset (Ctrl+D) une fois par profession " +
                "que tu veux créditer explicitement (ex. ProfessionPointReward_Livreur).",
                "Écraser", "Annuler");
            if (!overwrite) return;
            AssetDatabase.DeleteAsset(path);
        }
        var inst = ScriptableObject.CreateInstance<ProfessionPointRewardKind>();
        inst.name = "ProfessionPointReward";
        AssetDatabase.CreateAsset(inst, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = inst;
        EditorGUIUtility.PingObject(inst);
        Debug.Log($"[RewardKindAssetGenerator] Created neutral ProfessionPointReward at {path}. " +
                  "Duplique cet asset (Ctrl+D) et assigne `profession` par variant.");
    }

    private static void EnsureDir(string path) {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }

    private struct Spec {
        public string fileName;
        public System.Type type;
        public Spec(string fileName, System.Type type) { this.fileName = fileName; this.type = type; }
    }
}
#endif
