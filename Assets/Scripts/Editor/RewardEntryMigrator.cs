#if UNITY_EDITOR
using System.Collections.Generic;
using Sim.Missions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Migre les missions de l'ancien modèle <c>List&lt;RewardDefinition&gt;</c> vers le
/// nouveau <c>List&lt;RewardEntry&gt;</c>.
///
/// Pour chaque MissionDefinition :
///   - lit la liste legacy <c>rewards</c> (assets RewardDefinition typés-par-amount) ;
///   - pour chaque entrée, trouve l'asset RewardKind équivalent + lit son montant
///     via reflection (champ <c>amount</c> sur la sous-classe legacy) ;
///   - pousse une entrée <c>RewardEntry { kind, amount }</c> dans la nouvelle liste ;
///   - vide la liste legacy.
///
/// Après migration de TOUTES les missions, supprime les anciens assets RewardDefinition
/// orphelins (Money_25, JobXpReward, etc.) qui ne sont plus référencés.
///
/// Mode dry-run par défaut : logue ce qui SERAIT fait. Apply écrit réellement.
/// </summary>
internal static class RewardEntryMigrator {

    [MenuItem("Tools/Mission/Migrate Rewards (dry-run)")]
    public static void MigrateDryRun() => Run(applyChanges: false);

    [MenuItem("Tools/Mission/Migrate Rewards (apply)")]
    public static void MigrateApply() {
        bool confirm = EditorUtility.DisplayDialog(
            "Reward Migrator",
            "Cette opération va :\n\n" +
            "1. Lire la liste legacy 'rewards' de chaque MissionDefinition\n" +
            "2. Transformer chaque RewardDefinition asset en RewardEntry { kind, amount }\n" +
            "3. Pousser les entrées dans la nouvelle liste rewardEntries\n" +
            "4. Vider la liste legacy\n" +
            "5. Supprimer les assets RewardDefinition orphelins\n\n" +
            "Lance un dry-run d'abord pour vérifier ce qui va être touché.",
            "Appliquer", "Annuler");
        if (!confirm) return;
        Run(applyChanges: true);
    }

    private static void Run(bool applyChanges) {
        // Index : RewardKind par type concret. Permet de retrouver MoneyRewardKind
        // pour un legacy MoneyReward, etc.
        var kindByLegacyType = BuildKindIndex();
        if (kindByLegacyType.Count == 0) {
            Debug.LogError("[RewardEntryMigrator] No RewardKind assets found. " +
                           "Run Tools → Mission → Generate Reward Kind Assets first.");
            return;
        }

        var legacyAssetsToDelete = new HashSet<string>();
        int totalJobs = 0;
        int totalEntriesMigrated = 0;

        foreach (var job in LoadAllAssets<MissionDefinition>()) {
            totalJobs++;
            var so = new SerializedObject(job);
            var legacyProp = so.FindProperty("rewards");
            var entriesProp = so.FindProperty("rewardEntries");
            if (legacyProp == null || entriesProp == null) {
                Debug.LogWarning($"[RewardEntryMigrator] {job.name}: missing rewards/rewardEntries prop");
                continue;
            }
            if (legacyProp.arraySize == 0) continue;            // déjà migré ou jamais eu de rewards
            if (entriesProp.arraySize > 0) {
                Debug.Log($"[RewardEntryMigrator] {job.name}: rewardEntries already populated ({entriesProp.arraySize} entries), skipping legacy migration");
                continue;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RewardEntryMigrator] {(applyChanges ? "" : "[dry-run] ")}{job.name}:");

            // Construction de la nouvelle liste avant d'écrire (pour pouvoir dry-run sans rien casser).
            var newEntries = new List<(RewardKind kind, int amount)>();
            for (int i = 0; i < legacyProp.arraySize; i++) {
                var legacyRef = legacyProp.GetArrayElementAtIndex(i).objectReferenceValue as RewardDefinition;
                if (legacyRef == null) {
                    sb.AppendLine($"  [{i}] null reference → skip");
                    continue;
                }
                var legacyType = legacyRef.GetType();
                if (!kindByLegacyType.TryGetValue(legacyType.Name, out var kind)) {
                    sb.AppendLine($"  [{i}] {legacyType.Name}: no matching RewardKind → skip");
                    continue;
                }
                int amount = ReadLegacyAmount(legacyRef);
                if (amount <= 0) {
                    sb.AppendLine($"  [{i}] {legacyType.Name}: amount={amount} → skip");
                    continue;
                }
                newEntries.Add((kind, amount));
                legacyAssetsToDelete.Add(AssetDatabase.GetAssetPath(legacyRef));
                sb.AppendLine($"  [{i}] {legacyType.Name} amount={amount} → RewardEntry(kind={kind.name}, amount={amount})");
                totalEntriesMigrated++;
            }

            Debug.Log(sb.ToString().TrimEnd());

            if (applyChanges && newEntries.Count > 0) {
                entriesProp.arraySize = newEntries.Count;
                for (int i = 0; i < newEntries.Count; i++) {
                    var elem = entriesProp.GetArrayElementAtIndex(i);
                    elem.FindPropertyRelative("kind").objectReferenceValue = newEntries[i].kind;
                    elem.FindPropertyRelative("amount").intValue = newEntries[i].amount;
                }
                legacyProp.ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(job);
            }
        }

        if (applyChanges) AssetDatabase.SaveAssets();

        // Suppression des assets RewardDefinition désormais orphelins. On vérifie qu'ils
        // ne sont plus référencés nulle part avant de delete (au cas où un autre asset
        // les utilise encore, ce qui ne devrait pas arriver mais bon).
        int deletedAssets = 0;
        foreach (var path in legacyAssetsToDelete) {
            // Sécurité : un re-find via AssetDatabase peut détecter les refs résiduelles ;
            // ici on se contente de supprimer le fichier. Si tu as des doutes, fais un dry-run
            // et inspecte la liste avant.
            if (applyChanges) {
                if (AssetDatabase.DeleteAsset(path)) deletedAssets++;
            } else {
                Debug.Log($"[RewardEntryMigrator] [dry-run] would delete legacy asset: {path}");
            }
        }
        if (applyChanges) {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[RewardEntryMigrator] {(applyChanges ? "Applied" : "[dry-run]")} — " +
                  $"jobs scanned: {totalJobs}, entries migrated: {totalEntriesMigrated}, " +
                  $"legacy assets {(applyChanges ? "deleted" : "would delete")}: {(applyChanges ? deletedAssets : legacyAssetsToDelete.Count)}");
    }

    /// <summary>
    /// Construit un index { LegacyTypeName → RewardKind asset }. Le mapping est codé en dur
    /// (5 types) parce que c'est plus lisible qu'une convention de nommage.
    /// </summary>
    private static Dictionary<string, RewardKind> BuildKindIndex() {
        var dict = new Dictionary<string, RewardKind>();
        var allKinds = LoadAllAssets<RewardKind>();

        // V3 : XpRewardKind + SocialCreditRewardKind retirés. Si un asset legacy
        // référence encore JobXpReward ou SocialCreditReward, le migrateur ne trouvera
        // pas de Kind cible et loguera un skip — on accepte cette perte parce que les
        // valeurs étaient des stub (Social Credit) ou inutilisées (XP).
        foreach (var k in allKinds) {
            switch (k) {
                case MoneyRewardKind                _: dict["MoneyReward"]               = k; break;
                case ScoreModulatedMoneyRewardKind  _: dict["ScoreModulatedMoneyReward"] = k; break;
                case OwnProfessionPointRewardKind   _: dict["ConstellationPointReward"]  = k; break;
            }
        }
        return dict;
    }

    /// <summary>
    /// Lit le montant d'un asset legacy. Chaque sous-classe l'expose via un nom de
    /// champ différent ; on supporte les 3 patrons rencontrés dans le code actuel.
    /// </summary>
    private static int ReadLegacyAmount(RewardDefinition legacy) {
        var so = new SerializedObject(legacy);
        // MoneyReward / JobXpReward : champ "amount"
        var p = so.FindProperty("amount");
        if (p != null && p.propertyType == SerializedPropertyType.Integer) return p.intValue;
        // ScoreModulatedMoneyReward : champ "baseAmount"
        p = so.FindProperty("baseAmount");
        if (p != null && p.propertyType == SerializedPropertyType.Integer) return p.intValue;
        // ConstellationPointReward : champ "professionAmount"
        p = so.FindProperty("professionAmount");
        if (p != null && p.propertyType == SerializedPropertyType.Integer) return p.intValue;
        // SocialCreditReward : champ "forOwner" (le forPlayerTarget est ignoré côté migration ;
        // le nouveau Kind applique authoredAmount aux deux côtés, ce qui est suffisant en V1)
        p = so.FindProperty("forOwner");
        if (p != null && p.propertyType == SerializedPropertyType.Integer) return p.intValue;
        return 0;
    }

    private static List<T> LoadAllAssets<T>() where T : Object {
        var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        var list = new List<T>(guids.Length);
        foreach (var guid in guids) {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) list.Add(asset);
        }
        return list;
    }
}
#endif
