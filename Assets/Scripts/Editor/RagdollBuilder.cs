#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Construit un rig ragdoll fonctionnel sur un prefab humanoïde (Rigidbody + Collider + CharacterJoint
/// par os) et ajoute un <see cref="RagdollController"/>. Les os utilisent l'Animator humanoïde
/// (GetBoneTransform) — donc indépendant du nommage exact du squelette (Mixamo, etc.).
///
/// Les colliders d'os sont placés sur le layer « Ragdoll » pour ne JAMAIS être détectés par le
/// balayage de renversement du véhicule (qui ne vise que Player/NPC) — évite les re-impacts.
///
/// Idempotent : si les hanches portent déjà un Rigidbody, on considère le rig déjà construit et on
/// ne fait qu'assurer la présence du RagdollController. Le résultat est volontairement simple
/// (capsules) — affinable ensuite avec le Ragdoll Wizard d'Unity ; RagdollController marche avec
/// n'importe quel rig.
/// </summary>
public static class RagdollBuilder {
    [MenuItem("Tools/Vehicle/Build Ragdoll On Selected Prefab")]
    private static void BuildOnSelection() {
        foreach (Object o in Selection.objects) {
            string path = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab")) Build(path);
        }
    }

    /// <summary>Construit le ragdoll sur le prefab au chemin donné. Appelable par script (MCP).</summary>
    public static string Build(string prefabPath) {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return $"[RagdollBuilder] prefab introuvable: {prefabPath}";

        try {
            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
                return $"[RagdollBuilder] {prefabPath}: pas d'Animator humanoïde, ragdoll ignoré.";

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return $"[RagdollBuilder] {prefabPath}: os Hips introuvable.";

            bool alreadyBuilt = hips.GetComponent<Rigidbody>() != null;
            if (!alreadyBuilt) {
                RagdollRigBuilder.Build(animator);
            }

            if (root.GetComponent<RagdollController>() == null) root.AddComponent<RagdollController>();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return $"[RagdollBuilder] OK {prefabPath} (alreadyBuilt={alreadyBuilt})";
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

}
#endif
