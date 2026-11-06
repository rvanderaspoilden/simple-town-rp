#if UNITY_EDITOR
using AdvancedPeopleSystem;
using Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Outillage one-shot du rework personnages → Advanced People System 2 :
///  - <see cref="SetupPlayerPrefab"/> / <see cref="SetupNpcPrefabs"/> : remplace l'ancien rig du
///    prefab (enfant « Render ») par un rig APS2 pré-construit (male), migre l'avatar sur
///    l'Animator racine, recrée ancres (+ ragdoll si RagdollController présent), pose
///    CharacterAppearance et recâble PlayerHands/PlayerController quand ils existent.
///  - <see cref="SetupMainMenuPreview"/> : pose le preview APS2 du Main Menu et recâble
///    CharacterCreationManager + HomePanel.
///  - <see cref="ConvertApsMaterialsToUrp"/> : bascule les matériaux du pack (Built-in) vers URP.
/// Idempotent : relançable (le rig APS2 existant est reconstruit proprement).
/// </summary>
public static class ApsPlayerRigSetup {
    private const string PlayerPrefabPath = "Assets/Resources/Prefabs/Characters/Character.prefab";
    private static readonly string[] NpcPrefabPaths = {
        "Assets/Resources/Prefabs/NPC/Client/NPC Client 1.prefab",
        "Assets/Resources/Prefabs/NPC/Server/NPC Server 1.prefab",
    };
    private const string MaleSettingsPath = "Assets/AdvancedPeopleSystem2/Settings/MaleSettings.asset";
    private const string FemaleSettingsPath = "Assets/AdvancedPeopleSystem2/Settings/FemaleSettings.asset";
    private const string DemoPrefabPath = "Assets/AdvancedPeopleSystem2/Prefabs/CustomizableCharacter.prefab";
    private const string MainMenuScenePath = "Assets/Scenes/Main Menu.unity";

    [MenuItem("Tools/The BROZ/APS2/Setup Player Prefab")]
    private static void SetupPlayerPrefabMenu() => Debug.Log(SetupPlayerPrefab());

    [MenuItem("Tools/The BROZ/APS2/Setup NPC Prefabs")]
    private static void SetupNpcPrefabsMenu() => Debug.Log(SetupNpcPrefabs());

    [MenuItem("Tools/The BROZ/APS2/Setup Main Menu Preview")]
    private static void SetupMainMenuPreviewMenu() => Debug.Log(SetupMainMenuPreview());

    [MenuItem("Tools/The BROZ/APS2/Convert Pack Materials To URP")]
    private static void ConvertMaterialsMenu() => Debug.Log(ConvertApsMaterialsToUrp());

    public static string SetupPlayerPrefab() => SetupCharacterPrefab(PlayerPrefabPath);

    public static string SetupNpcPrefabs() {
        string result = string.Empty;
        foreach (string path in NpcPrefabPaths) {
            result += SetupCharacterPrefab(path) + "\n";
        }
        return result;
    }

    /// <summary>Remplace le rig de l'enfant « Render » par un rig APS2 male pré-construit et pose
    /// tout le câblage (Animator racine, ancres, CharacterAppearance, ragdoll si applicable).</summary>
    public static string SetupCharacterPrefab(string prefabPath) {
        var male = AssetDatabase.LoadAssetAtPath<CharacterSettings>(MaleSettingsPath);
        var female = AssetDatabase.LoadAssetAtPath<CharacterSettings>(FemaleSettingsPath);
        if (male == null || female == null) return "[ApsPlayerRigSetup] settings APS2 introuvables";

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try {
            Animator rootAnimator = root.GetComponent<Animator>();
            if (rootAnimator == null) return $"[ApsPlayerRigSetup] {prefabPath}: pas d'Animator racine";
            RuntimeAnimatorController gameplayController = rootAnimator.runtimeAnimatorController;

            // L'ancien CharacterStyleSetup (classe supprimée) survit en « missing script ».
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);

            // ── Capture des réglages de l'ancien rig avant destruction ─────────────────
            Vector3 handPos = new Vector3(0f, 0.08f, 0.03f);
            Vector3 handEuler = new Vector3(270f, 180f, 0f);
            Vector3 backPos = Vector3.zero;
            Vector3 backEuler = Vector3.zero;
            float camHeight = 1.45f;
            int renderLayer = root.layer;

            Transform oldRender = root.transform.Find("Render");
            if (oldRender != null) {
                renderLayer = oldRender.gameObject.layer;
                Transform oldHand = FindDeep(oldRender, "Handle Position");
                if (oldHand != null) { handPos = oldHand.localPosition; handEuler = oldHand.localEulerAngles; }
                Transform oldBack = FindDeep(oldRender, "Back Anchor");
                if (oldBack != null) { backPos = oldBack.localPosition; backEuler = oldBack.localEulerAngles; }
                Transform oldCam = FindDeep(oldRender, "TargetForCamera");
                if (oldCam != null) camHeight = oldCam.localPosition.y;

                Object.DestroyImmediate(oldRender.gameObject);
            }

            // ── Nouveau rig APS2 (male pré-construit) sous « Render » ──────────────────
            var renderGO = new GameObject("Render");
            renderGO.layer = renderLayer;
            renderGO.transform.SetParent(root.transform, false);
            renderGO.transform.SetSiblingIndex(0);

            var cc = renderGO.AddComponent<CharacterCustomization>();
            cc.selectedsettings = male;
            cc.InitializeMeshes(male, false);
            SetLayerRecursively(renderGO.transform, renderLayer);

            // FeetOffset du pack (offsets de chaussures) copié depuis le prefab de démo.
            var demo = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPrefabPath);
            var srcFeet = demo != null ? demo.GetComponent<FeetOffset>() : null;
            if (srcFeet != null) {
                var dstFeet = renderGO.AddComponent<FeetOffset>();
                EditorUtility.CopySerialized(srcFeet, dstFeet);
            }

            // ── Animator : l'avatar APS2 migre sur la racine (IK, NetworkAnimator…) ───
            Animator rigAnimator = renderGO.GetComponent<Animator>();
            rootAnimator.avatar = male.Avatar;
            rootAnimator.runtimeAnimatorController = gameplayController;
            if (rigAnimator != null && rigAnimator != rootAnimator) Object.DestroyImmediate(rigAnimator);
            cc.animator = rootAnimator;
            rootAnimator.Rebind();

            // ── Ancres mains/dos/caméra sur les nouveaux os ────────────────────────────
            Transform rightHand = rootAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform leftHand = rootAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform chest = rootAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = rootAnimator.GetBoneTransform(HumanBodyBones.Chest);
            if (rightHand == null || leftHand == null) return $"[ApsPlayerRigSetup] {prefabPath}: os mains introuvables (avatar non humanoïde ?)";

            Transform rightAnchor = CreateAnchor(rightHand, "Handle Position", handPos, handEuler);
            Transform leftAnchor = CreateAnchor(leftHand, "Handle Position", handPos, handEuler);
            Transform backAnchor = CreateAnchor(chest, "Back Anchor", backPos, backEuler);
            Transform camTarget = CreateAnchor(renderGO.transform, "TargetForCamera", new Vector3(0f, camHeight, 0f), Vector3.zero);

            var playerHands = root.GetComponent<PlayerHands>();
            if (playerHands != null) {
                var hands = new SerializedObject(playerHands);
                hands.FindProperty("leftHandTransform").objectReferenceValue = leftAnchor;
                hands.FindProperty("rightHandTransform").objectReferenceValue = rightAnchor;
                hands.FindProperty("backTransform").objectReferenceValue = backAnchor;
                hands.ApplyModifiedPropertiesWithoutUndo();
            }

            var playerController = root.GetComponent<PlayerController>();
            if (playerController != null) {
                var controller = new SerializedObject(playerController);
                controller.FindProperty("headTargetForCamera").objectReferenceValue = camTarget;
                controller.ApplyModifiedPropertiesWithoutUndo();
            }

            // ── CharacterAppearance ─────────────────────────────────────────────────────
            var appearance = root.GetComponent<CharacterAppearance>();
            if (appearance == null) appearance = root.AddComponent<CharacterAppearance>();
            var app = new SerializedObject(appearance);
            app.FindProperty("customization").objectReferenceValue = cc;
            app.FindProperty("maleSettings").objectReferenceValue = male;
            app.FindProperty("femaleSettings").objectReferenceValue = female;
            app.FindProperty("animatorController").objectReferenceValue = gameplayController;
            app.FindProperty("handAnchorLocalPosition").vector3Value = handPos;
            app.FindProperty("handAnchorLocalEuler").vector3Value = handEuler;
            app.FindProperty("backAnchorLocalPosition").vector3Value = backPos;
            app.FindProperty("backAnchorLocalEuler").vector3Value = backEuler;
            app.FindProperty("cameraTargetHeight").floatValue = camHeight;
            app.ApplyModifiedPropertiesWithoutUndo();

            // ── Ragdoll sur le nouveau squelette (seulement si le prefab en porte un) ──
            bool ragdollOk = false;
            if (root.GetComponent<RagdollController>() != null) {
                ragdollOk = RagdollRigBuilder.Build(rootAnimator);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return $"[ApsPlayerRigSetup] OK {prefabPath} — rig APS2 male, ancres recréées, ragdoll={ragdollOk}, camHeight={camHeight}";
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string SetupMainMenuPreview() {
        var male = AssetDatabase.LoadAssetAtPath<CharacterSettings>(MaleSettingsPath);
        var female = AssetDatabase.LoadAssetAtPath<CharacterSettings>(FemaleSettingsPath);
        if (male == null || female == null) return "[ApsPlayerRigSetup] settings APS2 introuvables";

        var scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

        if (Object.FindObjectsByType<CharacterAppearance>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
            return "[ApsPlayerRigSetup] Main Menu déjà migré";

        var oldAnims = Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        GameObject old = null;
        foreach (var a in oldAnims) {
            if (a.gameObject.name == "Character" && a.transform.parent == null) { old = a.gameObject; break; }
        }
        if (old == null) return "[ApsPlayerRigSetup] GO preview « Character » introuvable dans Main Menu";

        Transform oldT = old.transform;
        var oldAnim = old.GetComponent<Animator>();
        RuntimeAnimatorController previewController = oldAnim != null ? oldAnim.runtimeAnimatorController : null;

        // ── Nouveau preview APS2 (tout sur la racine : le rig est son seul contenu) ────
        var previewGO = new GameObject(old.name);
        previewGO.transform.SetParent(oldT.parent, false);
        previewGO.transform.SetLocalPositionAndRotation(oldT.localPosition, oldT.localRotation);
        previewGO.transform.localScale = oldT.localScale;
        previewGO.layer = old.layer;

        previewGO.AddComponent<Animator>();
        var cc = previewGO.AddComponent<CharacterCustomization>();
        cc.selectedsettings = male;
        cc.InitializeMeshes(male, false);
        SetLayerRecursively(previewGO.transform, old.layer);

        Animator anim = previewGO.GetComponent<Animator>();
        if (previewController != null) anim.runtimeAnimatorController = previewController;
        anim.Rebind();

        var appearance = previewGO.AddComponent<CharacterAppearance>();
        var app = new SerializedObject(appearance);
        app.FindProperty("customization").objectReferenceValue = cc;
        app.FindProperty("maleSettings").objectReferenceValue = male;
        app.FindProperty("femaleSettings").objectReferenceValue = female;
        app.FindProperty("animatorController").objectReferenceValue = previewController;
        app.ApplyModifiedPropertiesWithoutUndo();

        // ── Recâblage des managers ──────────────────────────────────────────────────────
        foreach (var m in Object.FindObjectsByType<CharacterCreationManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            var so = new SerializedObject(m);
            so.FindProperty("characterAppearance").objectReferenceValue = appearance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var m in Object.FindObjectsByType<HomePanel>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            var so = new SerializedObject(m);
            so.FindProperty("characterAppearance").objectReferenceValue = appearance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Object.DestroyImmediate(old);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "[ApsPlayerRigSetup] OK Main Menu — preview APS2 posé et managers recâblés";
    }

    /// <summary>Le pack APS2 est livré en Built-in RP (magenta sous URP) : bascule les matériaux
    /// body sur le shader palette URP maison (Sim/ApsBodyPalette, mêmes propriétés) et convertit
    /// tous les matériaux Standard du pack vers Universal Render Pipeline/Lit.</summary>
    public static string ConvertApsMaterialsToUrp() {
        Shader palette = Shader.Find("Sim/ApsBodyPalette");
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (palette == null) return "[ApsPlayerRigSetup] shader Sim/ApsBodyPalette introuvable";

        int bodies = 0, converted = 0, skipped = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/AdvancedPeopleSystem2" })) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            if (mat.shader.name == "AdvancedPeoplePack/BodyPaletteShader") {
                mat.shader = palette;
                EditorUtility.SetDirty(mat);
                bodies++;
            } else if (mat.shader.name == "Standard") {
                Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                float smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
                float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
                bool transparent = mat.IsKeywordEnabled("_ALPHABLEND_ON") || mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent;

                mat.shader = urpLit;
                mat.SetTexture("_BaseMap", mainTex);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                if (transparent) {
                    mat.SetFloat("_Surface", 1f);
                    mat.SetFloat("_Blend", 0f);
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                EditorUtility.SetDirty(mat);
                converted++;
            } else {
                skipped++;
            }
        }
        AssetDatabase.SaveAssets();
        return $"[ApsPlayerRigSetup] matériaux URP : body={bodies} standard→lit={converted} ignorés={skipped}";
    }

    private static Transform CreateAnchor(Transform parent, string name, Vector3 localPosition, Vector3 localEuler) {
        if (parent == null) return null;
        Transform anchor = parent.Find(name);
        if (anchor == null) {
            anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
        }
        anchor.localPosition = localPosition;
        anchor.localEulerAngles = localEuler;
        return anchor;
    }

    private static Transform FindDeep(Transform parent, string name) {
        foreach (Transform child in parent) {
            if (child.name == name) return child;
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void SetLayerRecursively(Transform t, int layer) {
        t.gameObject.layer = layer;
        foreach (Transform child in t) SetLayerRecursively(child, layer);
    }
}
#endif
