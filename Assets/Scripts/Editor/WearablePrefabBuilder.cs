using UnityEditor;
using UnityEngine;

/// <summary>
/// Construit les prefabs monde des items vêtement/sac à dos (consommé par
/// <see cref="ClothingConfigGenerator"/> et <see cref="BackpackItemGenerator"/>) :
/// racine (layer Items, tag Interactable, ItemIdentity + behaviour + BoxCollider) avec deux
/// visuels enfants — « Folded » (pile de linge Synty, visuel TENU EN MAIN) et « Model »
/// (vrai modèle 3D, visuel AU SOL, absent pour les vêtements en tissu). Chaque visuel est
/// recentré pivot BAS-CENTRE (le drop pose le pivot au sol ; les meshes APS2 sont en bind
/// pose à l'origine du personnage, les props Synty ont leur propre pivot).
/// Idempotent : SaveAsPrefabAsset sur le même chemin conserve le GUID.
/// </summary>
public static class WearablePrefabBuilder {
    public const string FoldedSourcePath = "Assets/Synty/PolygonShops/Prefabs/Props/SM_Prop_Clothes_Folded_01.prefab";
    private const int ItemLayer = 19;

    /// <summary>Grip main droite partagé par TOUS les vêtements/sacs tenus : le visuel en main
    /// est toujours la même pile pliée, un seul réglage suffit (écrit dans chaque config).
    /// Valeurs calées sur la pose BOX échantillonnée : pile à plat entre les deux paumes,
    /// alignée au personnage (pivot bas-centre au milieu des mains, -6 cm, +5 cm en avant).</summary>
    public static readonly Vector3 HeldGripPosition = new Vector3(0.072f, 0.160f, -0.057f);
    public static readonly Vector3 HeldGripEuler = new Vector3(343.4f, 346.5f, 235.5f);

    public static bool TryGetFoldedMesh(out Mesh mesh, out Material[] mats) {
        mesh = null;
        mats = null;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(FoldedSourcePath);
        var filter = source != null ? source.GetComponentInChildren<MeshFilter>() : null;
        var renderer = source != null ? source.GetComponentInChildren<MeshRenderer>() : null;
        if (filter == null || filter.sharedMesh == null || renderer == null) {
            Debug.LogError($"[WearablePrefabBuilder] Folded source invalid: {FoldedSourcePath}");
            return false;
        }
        mesh = filter.sharedMesh;
        mats = renderer.sharedMaterials;
        return true;
    }

    /// <summary>Construit/met à jour le prefab : <paramref name="modelMesh"/> null = vêtement en
    /// tissu (plié au sol aussi). Retourne le behaviour du prefab sauvegardé (à référencer
    /// dans la config).</summary>
    public static T Build<T>(string path, string name, Mesh modelMesh, Material[] modelMats,
        Mesh foldedMesh, Material[] foldedMats) where T : ItemBehaviour
    {
        var root = new GameObject(name) { layer = ItemLayer, tag = "Interactable" };

        GameObject folded = BuildVisual("Folded", root.transform, foldedMesh, foldedMats);
        GameObject model = modelMesh != null ? BuildVisual("Model", root.transform, modelMesh, modelMats) : null;

        // État initial = posé au sol : modèle visible (ou plié si tissu). L'attach en main
        // bascule via ClothingItemBehaviour.ApplyFoldedVisual.
        folded.SetActive(model == null);
        if (model != null) model.SetActive(true);

        // Collider sur le visuel SOL (c'est l'état cliquable/hoverable au sol).
        Mesh groundMesh = modelMesh != null ? modelMesh : foldedMesh;
        var collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, groundMesh.bounds.size.y * 0.5f, 0f);
        collider.size = groundMesh.bounds.size;

        root.AddComponent<ItemIdentity>();
        var behaviour = root.AddComponent<T>();

        var so = new SerializedObject(behaviour);
        so.FindProperty("foldedVisual").objectReferenceValue = folded;
        so.FindProperty("modelVisual").objectReferenceValue = model;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<T>();
    }

    private static GameObject BuildVisual(string name, Transform parent, Mesh mesh, Material[] mats) {
        var go = new GameObject(name) { layer = ItemLayer };
        go.transform.SetParent(parent, false);
        Bounds b = mesh.bounds;
        go.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = PadMaterials(mats, mesh.subMeshCount);
        return go;
    }

    public static Material[] PadMaterials(Material[] source, int subMeshCount) {
        var mats = new Material[subMeshCount];
        for (int i = 0; i < subMeshCount; i++) {
            mats[i] = source[Mathf.Min(i, source.Length - 1)];
        }
        return mats;
    }
}
