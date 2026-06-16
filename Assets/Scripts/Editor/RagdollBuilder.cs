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
    private const string RagdollLayerName = "Ragdoll";

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

            int ragdollLayer = LayerMask.NameToLayer(RagdollLayerName);

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return $"[RagdollBuilder] {prefabPath}: os Hips introuvable.";

            bool alreadyBuilt = hips.GetComponent<Rigidbody>() != null;
            if (!alreadyBuilt) {
                BuildRig(animator, ragdollLayer);
            }

            if (root.GetComponent<RagdollController>() == null) root.AddComponent<RagdollController>();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return $"[RagdollBuilder] OK {prefabPath} (alreadyBuilt={alreadyBuilt})";
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildRig(Animator a, int ragdollLayer) {
        Transform hips      = a.GetBoneTransform(HumanBodyBones.Hips);
        Transform spine     = a.GetBoneTransform(HumanBodyBones.Spine);
        Transform chest     = a.GetBoneTransform(HumanBodyBones.Chest) ?? spine;
        Transform head      = a.GetBoneTransform(HumanBodyBones.Head);
        Transform lUpArm    = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform lLoArm    = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform lHand     = a.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rUpArm    = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform rLoArm    = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform rHand     = a.GetBoneTransform(HumanBodyBones.RightHand);
        Transform lUpLeg    = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform lLoLeg    = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform lFoot     = a.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rUpLeg    = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform rLoLeg    = a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform rFoot     = a.GetBoneTransform(HumanBodyBones.RightFoot);

        // Torse : segment unique hanches→tête (porté par spine).
        AddCapsulePart(hips,   spine,  null,   2.6f, ragdollLayer);
        AddCapsulePart(spine,  head,   hips,   2.6f, ragdollLayer);
        AddSpherePart (head,           spine,  1.0f, ragdollLayer);

        AddCapsulePart(lUpArm, lLoArm, spine,  1.0f, ragdollLayer);
        AddCapsulePart(lLoArm, lHand,  lUpArm, 0.8f, ragdollLayer);
        AddCapsulePart(rUpArm, rLoArm, spine,  1.0f, ragdollLayer);
        AddCapsulePart(rLoArm, rHand,  rUpArm, 0.8f, ragdollLayer);

        AddCapsulePart(lUpLeg, lLoLeg, hips,   1.6f, ragdollLayer);
        AddCapsulePart(lLoLeg, lFoot,  lUpLeg, 1.2f, ragdollLayer);
        AddCapsulePart(rUpLeg, rLoLeg, hips,   1.6f, ragdollLayer);
        AddCapsulePart(rLoLeg, rFoot,  rUpLeg, 1.2f, ragdollLayer);
    }

    private static void AddCapsulePart(Transform bone, Transform child, Transform parent, float mass, int layer) {
        if (bone == null) return;
        if (layer >= 0) bone.gameObject.layer = layer;

        // Longueur = vecteur os→enfant (local) ; défaut si pas d'enfant.
        Vector3 dirLocal = child != null ? bone.InverseTransformPoint(child.position) : new Vector3(0f, -0.25f, 0f);
        float height = Mathf.Max(0.1f, dirLocal.magnitude);

        int axis = 1; // y par défaut
        float ax = Mathf.Abs(dirLocal.x), ay = Mathf.Abs(dirLocal.y), az = Mathf.Abs(dirLocal.z);
        if (ax >= ay && ax >= az) axis = 0;
        else if (az >= ax && az >= ay) axis = 2;

        CapsuleCollider cap = GetOrAdd<CapsuleCollider>(bone.gameObject);
        cap.direction = axis;
        cap.height = height;
        cap.radius = Mathf.Clamp(height * 0.22f, 0.04f, 0.18f);
        cap.center = dirLocal * 0.5f;

        AddBody(bone, parent, mass);
    }

    private static void AddSpherePart(Transform bone, Transform parent, float mass, int layer) {
        if (bone == null) return;
        if (layer >= 0) bone.gameObject.layer = layer;

        SphereCollider s = GetOrAdd<SphereCollider>(bone.gameObject);
        s.radius = 0.12f;
        s.center = new Vector3(0f, 0.1f, 0f);

        AddBody(bone, parent, mass);
    }

    private static void AddBody(Transform bone, Transform parent, float mass) {
        Rigidbody rb = GetOrAdd<Rigidbody>(bone.gameObject);
        rb.mass = mass;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.08f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.isKinematic = true; // repos = animé ; RagdollController bascule en dynamique au renversement

        if (parent != null) {
            Rigidbody parentBody = parent.GetComponent<Rigidbody>();
            CharacterJoint joint = GetOrAdd<CharacterJoint>(bone.gameObject);
            joint.connectedBody = parentBody;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;
            joint.enableProjection = true;
            joint.lowTwistLimit  = new SoftJointLimit { limit = -20f };
            joint.highTwistLimit = new SoftJointLimit { limit =  20f };
            joint.swing1Limit    = new SoftJointLimit { limit =  35f };
            joint.swing2Limit    = new SoftJointLimit { limit =  35f };
        }
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
#endif
