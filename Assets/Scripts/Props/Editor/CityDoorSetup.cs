#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Sim.Building;   // PropsRenderer
using Sim.Enums;      // DoorLockState
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SimEditor {
    /// <summary>
    /// Builds interactive wrapper prefabs for the Synty PolygonShops building/shop doors
    /// and swaps the City scene's static door instances for them.
    ///
    /// The Synty door meshes are single-piece slabs (frame provided by the wall), so each
    /// wrapper is a static controller root (PropIdentity + DoorBehaviour + DoorPropSource +
    /// PropsRenderer + trigger BoxCollider + NavMeshObstacle) with the original Synty door
    /// prefab nested as a child that serves as the swinging <c>doorBody</c>. This mirrors the
    /// hand-made Door / Front Door / Big Door prefabs: the server opens the door on proximity
    /// (DoorPropSource trigger), the client animates the leaf (DoorBehaviour). The nested
    /// Synty prefab keeps the art linked so future Synty updates propagate.
    ///
    /// Workflow:
    ///   1. Tools ▸ The BROZ ▸ Doors ▸ 1. Build Interactive Door Prefabs
    ///   2. Open City.unity, then Tools ▸ The BROZ ▸ Doors ▸ 2. Replace Active City Doors
    ///   3. Tools ▸ Props ▸ Generate Scene Prop IDs, then save the scene.
    /// </summary>
    public static class CityDoorSetup {
        private const string FoundationsFolder = "Assets/Resources/Prefabs/Props/Foundations";
        private const string SyntyDoorFolder   = "Assets/Synty/PolygonShops/Prefabs/Buildings";
        private const string OpenClipGuid       = "4f8aa9e57a7a5ed4295faa1a774724d0";
        private const string CloseClipGuid      = "952e8d12e090ef744a71c6f912f460d0";
        private const int    PropsLayer         = 10; // "Props"

        private class DoorDef {
            public string synty;        // Synty prefab base name (also the body mesh name)
            public float  width = 1f;   // door opening width (local X) for the trigger
            public bool   isDouble;
            public string leftLeaf;     // double only: child used as doorBody
            public string rightLeaf;    // double only: child used as secondaryDoorBody
        }

        private static readonly DoorDef[] Defs = {
            new DoorDef { synty = "SM_Bld_Door_02",          width = 1.05f },
            new DoorDef { synty = "SM_Bld_Door_03",          width = 1.0f  },
            new DoorDef { synty = "SM_Bld_Shopfront_Door_03", width = 1.0f  },
            new DoorDef { synty = "SM_Bld_Shopfront_Door_04", width = 1.0f  },
            new DoorDef { synty = "SM_Bld_Shopfront_Door_05", width = 1.0f  },
            new DoorDef { synty = "SM_Bld_Shopfront_Door_06", width = 1.0f  },
            new DoorDef { synty = "SM_Bld_Base_Door_01",      width = 1.0f  },
            new DoorDef {
                synty = "SM_Bld_Wall_Glass_Door_Double_01", width = 2.5f, isDouble = true,
                leftLeaf  = "SM_Bld_Wall_Glass_Door_Double_01_L",
                rightLeaf = "SM_Bld_Wall_Glass_Door_Double_01_R",
            },
        };

        // ── Step 1: build the wrapper prefabs ─────────────────────────────────────

        [MenuItem("Tools/The BROZ/Doors/1. Build Interactive Door Prefabs")]
        public static void BuildPrefabs() {
            AudioClip openClip  = LoadByGuid<AudioClip>(OpenClipGuid);
            AudioClip closeClip = LoadByGuid<AudioClip>(CloseClipGuid);

            int built = 0;
            var report = new List<string>();
            foreach (DoorDef d in Defs) {
                string syntyPath = $"{SyntyDoorFolder}/{d.synty}.prefab";
                GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(syntyPath);
                if (src == null) { report.Add($"⛔ missing {syntyPath}"); continue; }

                GameObject root = new GameObject(d.synty) { layer = PropsLayer };
                try {
                    // Nested Synty door = the swinging body (art stays linked).
                    GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                    body.transform.localPosition = Vector3.zero;
                    body.transform.localRotation = Quaternion.identity;
                    // Walk-through door: drop the solid Synty colliders (like the base Door prefab,
                    // which carries only the root trigger + a carving NavMeshObstacle).
                    foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                        Object.DestroyImmediate(c);

                    GameObject audioGo = new GameObject("Audio Source");
                    audioGo.transform.SetParent(root.transform, false);
                    audioGo.transform.localPosition = new Vector3(d.isDouble ? -1.25f : -0.5f, 1f, 0f);
                    AudioSource audio = audioGo.AddComponent<AudioSource>();
                    audio.playOnAwake = false;
                    audio.spatialBlend = 1f;
                    audio.volume = 0.6f;
                    audio.minDistance = 1.6f;
                    audio.maxDistance = 12f;
                    audio.rolloffMode = AudioRolloffMode.Linear;

                    PropIdentity   identity = root.AddComponent<PropIdentity>();   // roomId defaults to "city"
                    NavMeshObstacle obstacle = root.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.carving = true;
                    obstacle.carveOnlyStationary = true;
                    float ox = d.isDouble ? -1.25f : -0.5f;
                    float halfW = d.isDouble ? 1.25f : 0.55f;
                    obstacle.center = new Vector3(ox, 1f, 0f);
                    obstacle.size   = new Vector3(halfW * 2f, 2f, 0.2f);

                    BoxCollider trigger = root.AddComponent<BoxCollider>();
                    trigger.isTrigger = true;
                    trigger.center = new Vector3(ox, d.isDouble ? 1.3f : 1f, 0f);
                    trigger.size   = new Vector3(d.width + 0.1f, d.isDouble ? 2.6f : 2f, 1.7f);

                    DoorPropSource doorSrc = root.AddComponent<DoorPropSource>();
                    DoorBehaviour  behaviour = root.AddComponent<DoorBehaviour>();
                    PropsRenderer  renderer = root.AddComponent<PropsRenderer>();

                    // DoorPropSource.defaultLockState = UNLOCKED → auto-open on proximity, walk-through.
                    SerializedObject soS = new SerializedObject(doorSrc);
                    soS.FindProperty("defaultLockState").enumValueIndex = (int)DoorLockState.UNLOCKED;
                    soS.ApplyModifiedPropertiesWithoutUndo();

                    Transform leaf  = d.isDouble ? body.transform.Find(d.leftLeaf)  : body.transform;
                    Transform leaf2 = d.isDouble ? body.transform.Find(d.rightLeaf) : null;
                    if (d.isDouble && (leaf == null || leaf2 == null))
                        report.Add($"⚠ {d.synty}: leaf child not found (L={leaf}, R={leaf2})");

                    SerializedObject soB = new SerializedObject(behaviour);
                    soB.FindProperty("doorBody").objectReferenceValue = leaf;
                    soB.FindProperty("openedLocalRotation").vector3Value = new Vector3(0, -90, 0);
                    if (leaf2 != null) {
                        soB.FindProperty("secondaryDoorBody").objectReferenceValue = leaf2;
                        soB.FindProperty("secondaryOpenedLocalRotation").vector3Value = new Vector3(0, 90, 0);
                    }
                    soB.FindProperty("navMeshObstacle").objectReferenceValue = obstacle;
                    soB.FindProperty("audioSource").objectReferenceValue = audio;
                    soB.FindProperty("openClip").objectReferenceValue = openClip;
                    soB.FindProperty("closeClip").objectReferenceValue = closeClip;
                    soB.ApplyModifiedPropertiesWithoutUndo();

                    Renderer[] rends = body.GetComponentsInChildren<Renderer>(true)
                                           .Where(r => r is MeshRenderer).ToArray();
                    SerializedObject soR = new SerializedObject(renderer);
                    SerializedProperty arr = soR.FindProperty("renderersToModify");
                    arr.arraySize = rends.Length;
                    for (int i = 0; i < rends.Length; i++)
                        arr.GetArrayElementAtIndex(i).objectReferenceValue = rends[i];
                    SerializedProperty hide = soR.FindProperty("hideable");
                    if (hide != null) {
                        if (hide.propertyType == SerializedPropertyType.Boolean) hide.boolValue = true;
                        else hide.intValue = 1;
                    }
                    soR.ApplyModifiedPropertiesWithoutUndo();

                    string outPath = $"{FoundationsFolder}/{d.synty}.prefab";
                    PrefabUtility.SaveAsPrefabAsset(root, outPath);
                    report.Add($"✓ {outPath}  (renderers={rends.Length}{(d.isDouble ? ", double" : "")})");
                    built++;
                } finally {
                    Object.DestroyImmediate(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CityDoorSetup] Built {built}/{Defs.Length} interactive door prefabs:\n  " +
                      string.Join("\n  ", report));
        }

        // ── Step 2: replace the active City door instances ────────────────────────

        [MenuItem("Tools/The BROZ/Doors/2. Replace Active City Doors")]
        public static void ReplaceCityDoors() {
            var wrappers = new Dictionary<string, GameObject>();
            foreach (DoorDef d in Defs) {
                GameObject w = AssetDatabase.LoadAssetAtPath<GameObject>($"{FoundationsFolder}/{d.synty}.prefab");
                if (w != null) wrappers[d.synty] = w;
            }
            if (wrappers.Count == 0) {
                Debug.LogWarning("[CityDoorSetup] No wrapper prefabs found. Run step 1 first.");
                return;
            }

            // Targets: active door bodies that are their own (outermost) prefab-instance root.
            var targets = new List<(GameObject go, string type)>();
            foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (mf.sharedMesh == null) continue;
                string mn = mf.sharedMesh.name;
                if (!wrappers.ContainsKey(mn)) continue;
                GameObject go = mf.gameObject;
                if (!go.activeInHierarchy) continue;                              // only visible doors
                if (PrefabUtility.GetNearestPrefabInstanceRoot(go) != go) continue; // skip nested / already-wrapped
                targets.Add((go, mn));
            }

            int replaced = 0, failed = 0;
            var counts = new Dictionary<string, int>();
            foreach ((GameObject orig, string type) in targets) {
                GameObject wrapper = wrappers[type];
                Transform t = orig.transform;
                Transform parent = t.parent;
                int sib = t.GetSiblingIndex();
                Vector3 lp = t.localPosition; Quaternion lr = t.localRotation; Vector3 ls = t.localScale;
                try {
                    GameObject w = (GameObject)PrefabUtility.InstantiatePrefab(wrapper);
                    w.transform.SetParent(parent, false);
                    w.transform.localPosition = lp;
                    w.transform.localRotation = lr;
                    w.transform.localScale    = ls;
                    w.transform.SetSiblingIndex(sib);
                    Object.DestroyImmediate(orig);
                    replaced++;
                    counts[type] = counts.TryGetValue(type, out int c) ? c + 1 : 1;
                } catch (System.Exception e) {
                    Debug.LogError($"[CityDoorSetup] failed to replace '{orig.name}' ({type}): {e.Message}");
                    failed++;
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            string perType = string.Join("\n  ", counts.Select(kv => $"{kv.Key}: {kv.Value}"));
            Debug.Log($"[CityDoorSetup] Replaced {replaced} active door(s), {failed} failed.\n  {perType}\n" +
                      "Next: Tools ▸ Props ▸ Generate Scene Prop IDs, then save the scene.");
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private static T LoadByGuid<T>(string guid) where T : Object {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}
#endif
