using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Outil d'édition pour préparer en masse les props d'exposition d'un magasin
/// physique (scène City). Cible un GameObject racine (par défaut "Shop_center")
/// et, pour chaque PropBehaviourBase enfant :
///   - ajoute le composant ShopDisplay s'il manque
///   - applique un preset aléatoire (remise en % ou prix fixe)
///   - marque le prop static (StaticEditorFlags.Everything)
///   - retire les composants inutiles pour un prop d'expo figé
///     (Rigidbody, NavMeshObstacle, NavMeshAgent)
/// </summary>
public class ShopDisplaySetupTool : EditorWindow
{
    private const string DefaultRootName = "Shop_center";

    [System.Serializable]
    private struct DiscountPreset
    {
        public string label;
        public int discountPercent; // 0..100
        public int overridePrice;   // -1 = use discount
    }

    private GameObject _root;
    private bool _markStatic = true;
    private bool _stripRigidbody = true;
    private bool _stripNavMeshObstacle = true;
    private bool _stripNavMeshAgent = true;
    private bool _disableAudioSources = true;
    private bool _overwriteExistingShopDisplay = false;
    private int _randomSeed = 0;
    private bool _useFixedSeed = false;
    private Vector2 _scroll;

    private readonly List<DiscountPreset> _presets = new()
    {
        new DiscountPreset { label = "Plein tarif",        discountPercent = 0,  overridePrice = -1 },
        new DiscountPreset { label = "Petite remise -10%", discountPercent = 10, overridePrice = -1 },
        new DiscountPreset { label = "Remise -20%",        discountPercent = 20, overridePrice = -1 },
        new DiscountPreset { label = "Remise -30%",        discountPercent = 30, overridePrice = -1 },
        new DiscountPreset { label = "Promo -50%",         discountPercent = 50, overridePrice = -1 },
    };

    [MenuItem("Tools/Shop Display Setup")]
    public static void ShowWindow()
    {
        GetWindow<ShopDisplaySetupTool>("Shop Display Setup");
    }

    private void OnEnable()
    {
        if (_root == null)
        {
            var go = GameObject.Find(DefaultRootName);
            if (go != null) _root = go;
        }
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Cible", EditorStyles.boldLabel);
        _root = (GameObject)EditorGUILayout.ObjectField("Root GameObject", _root, typeof(GameObject), true);
        if (GUILayout.Button($"Auto-find \"{DefaultRootName}\""))
        {
            var go = GameObject.Find(DefaultRootName);
            if (go != null) _root = go;
            else EditorUtility.DisplayDialog("Shop Display Setup", $"GameObject \"{DefaultRootName}\" introuvable dans la scène active.", "OK");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        _markStatic = EditorGUILayout.Toggle("Marquer en static", _markStatic);
        _stripRigidbody = EditorGUILayout.Toggle("Retirer Rigidbody", _stripRigidbody);
        _stripNavMeshObstacle = EditorGUILayout.Toggle("Retirer NavMeshObstacle", _stripNavMeshObstacle);
        _stripNavMeshAgent = EditorGUILayout.Toggle("Retirer NavMeshAgent", _stripNavMeshAgent);
        _disableAudioSources = EditorGUILayout.Toggle("Désactiver AudioSource", _disableAudioSources);
        _overwriteExistingShopDisplay = EditorGUILayout.Toggle("Réécrire ShopDisplay existants", _overwriteExistingShopDisplay);

        EditorGUILayout.Space();
        _useFixedSeed = EditorGUILayout.Toggle("Graine fixe (reproductible)", _useFixedSeed);
        using (new EditorGUI.DisabledScope(!_useFixedSeed))
        {
            _randomSeed = EditorGUILayout.IntField("Seed", _randomSeed);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Presets de remise (tirage aléatoire)", EditorStyles.boldLabel);
        for (int i = 0; i < _presets.Count; i++)
        {
            var p = _presets[i];
            EditorGUILayout.BeginHorizontal();
            p.label = EditorGUILayout.TextField(p.label);
            p.discountPercent = EditorGUILayout.IntSlider(p.discountPercent, 0, 100);
            p.overridePrice = EditorGUILayout.IntField("Override", p.overridePrice);
            if (GUILayout.Button("X", GUILayout.Width(22))) { _presets.RemoveAt(i); i--; }
            else _presets[i] = p;
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("+ Ajouter un preset"))
        {
            _presets.Add(new DiscountPreset { label = "Custom", discountPercent = 0, overridePrice = -1 });
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(_root == null || _presets.Count == 0))
        {
            if (GUILayout.Button("Appliquer aux props enfants", GUILayout.Height(32)))
            {
                Apply();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void Apply()
    {
        if (_root == null) return;

        var propBehaviourType = FindPropBehaviourBaseType();
        if (propBehaviourType == null)
        {
            EditorUtility.DisplayDialog("Shop Display Setup", "Type PropBehaviourBase introuvable.", "OK");
            return;
        }

        var props = _root.GetComponentsInChildren(propBehaviourType, true)
            .Select(c => c.gameObject)
            .Distinct()
            .ToList();

        if (props.Count == 0)
        {
            EditorUtility.DisplayDialog("Shop Display Setup", "Aucun PropBehaviourBase trouvé sous la cible.", "OK");
            return;
        }

        var rng = _useFixedSeed ? new System.Random(_randomSeed) : new System.Random();

        Undo.SetCurrentGroupName("Shop Display Setup");
        int undoGroup = Undo.GetCurrentGroup();

        int added = 0, updated = 0, statics = 0, stripped = 0, audioDisabled = 0;

        var discountField = typeof(ShopDisplay).GetField("discountPercent", BindingFlags.Instance | BindingFlags.NonPublic);
        var overrideField = typeof(ShopDisplay).GetField("overridePrice",   BindingFlags.Instance | BindingFlags.NonPublic);

        foreach (var go in props)
        {
            var shop = go.GetComponent<ShopDisplay>();
            bool isNew = shop == null;
            if (isNew)
            {
                shop = Undo.AddComponent<ShopDisplay>(go);
                added++;
            }
            else if (!_overwriteExistingShopDisplay)
            {
                // ne pas toucher les valeurs existantes
                shop = null;
            }

            if (shop != null)
            {
                var preset = _presets[rng.Next(_presets.Count)];
                Undo.RecordObject(shop, "Apply Shop Preset");
                discountField?.SetValue(shop, Mathf.Clamp(preset.discountPercent, 0, 100));
                overrideField?.SetValue(shop, preset.overridePrice);
                EditorUtility.SetDirty(shop);
                if (!isNew) updated++;
            }

            if (_markStatic)
            {
                var current = GameObjectUtility.GetStaticEditorFlags(go);
                var desired = (StaticEditorFlags)~0;
                if (current != desired)
                {
                    Undo.RegisterCompleteObjectUndo(go, "Mark Static");
                    GameObjectUtility.SetStaticEditorFlags(go, desired);
                    statics++;
                }
            }

            if (_stripRigidbody)
                stripped += StripAll<Rigidbody>(go);
            if (_stripNavMeshObstacle)
                stripped += StripAll<NavMeshObstacle>(go);
            if (_stripNavMeshAgent)
                stripped += StripAll<NavMeshAgent>(go);

            if (_disableAudioSources)
            {
                foreach (var src in go.GetComponents<AudioSource>())
                {
                    if (!src.enabled) continue;
                    Undo.RecordObject(src, "Disable AudioSource");
                    src.enabled = false;
                    EditorUtility.SetDirty(src);
                    audioDisabled++;
                }
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

        Debug.Log($"[ShopDisplaySetupTool] Traité {props.Count} props — ShopDisplay ajoutés:{added}, mis à jour:{updated}, static:{statics}, composants retirés:{stripped}, AudioSource désactivés:{audioDisabled}");
        EditorUtility.DisplayDialog("Shop Display Setup",
            $"Terminé.\n\nProps: {props.Count}\nShopDisplay ajoutés: {added}\nMis à jour: {updated}\nMarqués static: {statics}\nComposants retirés: {stripped}\nAudioSource désactivés: {audioDisabled}",
            "OK");
    }

    private static int StripAll<T>(GameObject go) where T : Component
    {
        int count = 0;
        foreach (var c in go.GetComponents<T>())
        {
            Undo.DestroyObjectImmediate(c);
            count++;
        }
        return count;
    }

    private static System.Type FindPropBehaviourBaseType()
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("PropBehaviourBase");
            if (t != null) return t;
        }
        return null;
    }
}
