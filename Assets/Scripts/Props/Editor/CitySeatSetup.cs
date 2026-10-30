#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Sim.Building;   // PropsRenderer
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SimEditor {
    /// <summary>
    /// Builds sittable Prefab Variants for the Synty seat props (chairs, stools, benches,
    /// couches, booth seats) used in City and swaps the scene's static instances for them.
    ///
    /// Mirrors the hand-made Chair / Sofa / bench prefabs: a non-trigger BoxCollider (hover +
    /// physical block), a carving NavMeshObstacle, a kinematic Rigidbody, PropIdentity +
    /// SeatBehaviour + SeatPropSource + PropsRenderer, and one or more "Seat Position" anchor
    /// children. The server assigns a free slot on SIT; the client snaps the player to the
    /// matching anchor. Synty seats share a convention — backrest at −Z — so every anchor
    /// faces +Z (euler 0) at floor level, which matches the existing SIT animation calibration.
    ///
    /// Unlike the doors, these are real Prefab Variants (only additions, no restructuring), so
    /// the Synty art + collider stay linked and Synty updates propagate.
    ///
    /// Workflow:
    ///   1. Tools ▸ The BROZ ▸ Seats ▸ 1. Build Sittable Seat Variants
    ///   2. Open City.unity, then Tools ▸ The BROZ ▸ Seats ▸ 2. Replace Active City Seats
    ///   3. Tools ▸ Props ▸ Generate Scene Prop IDs, then save the scene.
    /// </summary>
    public static class CitySeatSetup {
        private const string SeatsFolder = "Assets/Resources/Prefabs/Props/Seats";
        private const int    PropsLayer  = 10; // "Props"

        private class SeatDef {
            public string synty;          // Synty prefab base name (also the source-prefab name)
            public int    seats = 1;      // number of seat slots
            public float  zOffset = 0.08f; // forward (+Z) offset of the anchor from the cushion centre
        }

        // Active, own-root seat types found in City. Counts are authored (width alone would
        // over-count armrest-heavy chairs like the barber/pedicure). Toilets are excluded.
        private static readonly SeatDef[] Defs = {
            new SeatDef { synty = "SM_Prop_Cafe_Chair_Folding_01",   seats = 1, zOffset = 0.08f },
            new SeatDef { synty = "SM_Prop_Cafe_Chair_02",           seats = 1, zOffset = 0.08f },
            new SeatDef { synty = "SM_Prop_Bar_Stool_01",            seats = 1, zOffset = 0.04f },
            new SeatDef { synty = "SM_Prop_Bar_Stool_02",            seats = 1, zOffset = 0.04f },
            new SeatDef { synty = "SM_Prop_Shop_Chair_01",           seats = 1, zOffset = 0.05f },
            new SeatDef { synty = "SM_Prop_Barber_Chair_01",         seats = 1, zOffset = 0.00f },
            new SeatDef { synty = "SM_Prop_Barber_Pedicure_Chair_01", seats = 1, zOffset = 0.00f },
            new SeatDef { synty = "SM_Prop_Couch_01",                seats = 1, zOffset = 0.08f }, // single armchair despite the name
            new SeatDef { synty = "SM_Prop_Bench_01",                seats = 3, zOffset = 0.08f },
            new SeatDef { synty = "SM_Prop_Bench_04",                seats = 3, zOffset = 0.08f },
            new SeatDef { synty = "SM_Prop_Bench_06",                seats = 3, zOffset = 0.08f },
            new SeatDef { synty = "SM_Prop_Bench_07",                seats = 3, zOffset = 0.10f },
            new SeatDef { synty = "SM_Prop_Bench_08",                seats = 3, zOffset = 0.08f },
            new SeatDef { synty = "SM_Prop_Bench_09",                seats = 3, zOffset = 0.08f },
            new SeatDef { synty = "SM_Prop_Bench_11",                seats = 3, zOffset = 0.08f },
            new SeatDef { synty = "SM_Prop_Clothes_Bench_Small_01",  seats = 3, zOffset = 0.06f },
            new SeatDef { synty = "SM_Prop_Cafe_Booth_Seat_01",      seats = 3, zOffset = 0.12f },
            new SeatDef { synty = "SM_Prop_Cafe_Booth_Seat_02",      seats = 2, zOffset = 0.12f },
            new SeatDef { synty = "SM_Prop_Cafe_Booth_Wall_Seat_01", seats = 3, zOffset = 0.12f },
        };

        // ── Step 1: build the sittable variants ───────────────────────────────────

        [MenuItem("Tools/The BROZ/Seats/1. Build Sittable Seat Variants")]
        public static void BuildVariants() {
            if (!AssetDatabase.IsValidFolder(SeatsFolder)) {
                AssetDatabase.CreateFolder("Assets/Resources/Prefabs/Props", "Seats");
            }

            int built = 0;
            var report = new List<string>();
            foreach (SeatDef d in Defs) {
                string syntyPath = FindSyntyPrefab(d.synty);
                if (syntyPath == null) { report.Add($"⛔ not found: {d.synty}"); continue; }
                GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(syntyPath);

                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                try {
                    // Whole hierarchy on Props so EVERY collider is hit by the interaction raycast
                    // (its mask includes Props but not Default; the interactable is resolved via
                    // GetComponentInParent, so child colliders are fine as long as the layer matches).
                    foreach (Transform tr in inst.GetComponentsInChildren<Transform>(true))
                        tr.gameObject.layer = PropsLayer;

                    // Local-space renderer bounds (for collider + anchor placement).
                    Bounds b = LocalBounds(inst);

                    // Keep the Synty collider(s). Only add a solid box when the prop has none.
                    if (inst.GetComponentInChildren<Collider>(true) == null) {
                        BoxCollider col = inst.AddComponent<BoxCollider>();
                        col.center = b.center;
                        col.size   = b.size;
                        col.isTrigger = false;
                    }

                    NavMeshObstacle obstacle = inst.GetComponent<NavMeshObstacle>();
                    if (obstacle == null) obstacle = inst.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.carving = true;
                    obstacle.carveOnlyStationary = true;
                    obstacle.center = new Vector3(b.center.x, Mathf.Max(b.center.y, b.extents.y), b.center.z);
                    obstacle.size   = new Vector3(b.size.x, b.size.y, b.size.z);

                    Rigidbody rb = inst.GetComponent<Rigidbody>();
                    if (rb == null) rb = inst.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;

                    EnsureComponent<PropIdentity>(inst);          // roomId defaults to "city"
                    SeatBehaviour seat   = EnsureComponent<SeatBehaviour>(inst);
                    EnsureComponent<SeatPropSource>(inst);
                    PropsRenderer renderer = EnsureComponent<PropsRenderer>(inst);

                    // Seat anchors spaced along X, floor level, facing +Z.
                    float spacing = Mathf.Min(0.62f, b.size.x * 0.72f / Mathf.Max(d.seats - 1, 1));
                    var anchors = new Transform[d.seats];
                    for (int i = 0; i < d.seats; i++) {
                        var anchorGo = new GameObject(i == 0 ? "Seat Position" : $"Seat Position ({i})");
                        anchorGo.transform.SetParent(inst.transform, false);
                        float x = (i - (d.seats - 1) * 0.5f) * spacing + b.center.x;
                        anchorGo.transform.localPosition = new Vector3(x, 0.03f, b.center.z + d.zOffset);
                        anchorGo.transform.localRotation = Quaternion.identity; // faces prop +Z
                        anchors[i] = anchorGo.transform;
                    }

                    SerializedObject soSeat = new SerializedObject(seat);
                    SerializedProperty seatArr = soSeat.FindProperty("seatTransforms");
                    seatArr.arraySize = anchors.Length;
                    for (int i = 0; i < anchors.Length; i++)
                        seatArr.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];
                    soSeat.FindProperty("couchTransforms").arraySize = 0;
                    soSeat.ApplyModifiedPropertiesWithoutUndo();

                    Renderer[] rends = inst.GetComponentsInChildren<Renderer>(true)
                                           .Where(r => r is MeshRenderer).ToArray();
                    SerializedObject soR = new SerializedObject(renderer);
                    SerializedProperty rArr = soR.FindProperty("renderersToModify");
                    rArr.arraySize = rends.Length;
                    for (int i = 0; i < rends.Length; i++)
                        rArr.GetArrayElementAtIndex(i).objectReferenceValue = rends[i];
                    SerializedProperty hide = soR.FindProperty("hideable");
                    if (hide != null) {
                        if (hide.propertyType == SerializedPropertyType.Boolean) hide.boolValue = true;
                        else hide.intValue = 1;
                    }
                    soR.ApplyModifiedPropertiesWithoutUndo();

                    string outPath = $"{SeatsFolder}/{d.synty}.prefab";
                    PrefabUtility.SaveAsPrefabAsset(inst, outPath); // instance of a prefab → Variant
                    report.Add($"✓ {d.synty}  seats={d.seats} width={b.size.x:F2} renderers={rends.Length}");
                    built++;
                } finally {
                    Object.DestroyImmediate(inst);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CitySeatSetup] Built {built}/{Defs.Length} sittable seat variants in {SeatsFolder}:\n  " +
                      string.Join("\n  ", report));
        }

        // ── Step 2: replace active City seat instances ────────────────────────────

        [MenuItem("Tools/The BROZ/Seats/2. Replace Active City Seats")]
        public static void ReplaceCitySeats() {
            var wrappers = new Dictionary<string, GameObject>();
            foreach (SeatDef d in Defs) {
                GameObject w = AssetDatabase.LoadAssetAtPath<GameObject>($"{SeatsFolder}/{d.synty}.prefab");
                if (w != null) wrappers[d.synty] = w;
            }
            if (wrappers.Count == 0) { Debug.LogWarning("[CitySeatSetup] No variants found. Run step 1 first."); return; }

            // Targets: active prefab instances whose source prefab is one of our seat types.
            var targets = new List<(GameObject go, string type)>();
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (!go.activeInHierarchy) continue;
                if (PrefabUtility.GetNearestPrefabInstanceRoot(go) != go) continue;         // outermost instance roots only
                GameObject srcAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (srcAsset == null) continue;
                string srcName = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(srcAsset));
                if (!wrappers.ContainsKey(srcName)) continue;
                // Skip things that are already our variant (its source is the Synty prefab too,
                // but it already carries a SeatBehaviour).
                if (go.GetComponent<SeatBehaviour>() != null) continue;
                targets.Add((go, srcName));
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
                    Debug.LogError($"[CitySeatSetup] failed to replace '{orig.name}' ({type}): {e.Message}");
                    failed++;
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            string perType = string.Join("\n  ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));
            Debug.Log($"[CitySeatSetup] Replaced {replaced} active seat(s), {failed} failed.\n  {perType}\n" +
                      "Next: Tools ▸ Props ▸ Generate Scene Prop IDs, then save the scene.");
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private static T EnsureComponent<T>(GameObject go) where T : Component {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static Bounds LocalBounds(GameObject root) {
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                // transform mesh-local bounds into root-local space
                Matrix4x4 m = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                Vector3 c = m.MultiplyPoint3x4(mb.center);
                Vector3 e = mb.extents;
                Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0, 0));
                Vector3 ay = m.MultiplyVector(new Vector3(0, e.y, 0));
                Vector3 az = m.MultiplyVector(new Vector3(0, 0, e.z));
                Vector3 ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                                          Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                                          Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
                Bounds wb = new Bounds(c, ext * 2f);
                if (any) b.Encapsulate(wb); else { b = wb; any = true; }
            }
            return b;
        }

        private static string FindSyntyPrefab(string name) {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab")) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name && path.Contains("/Synty/"))
                    return path;
            }
            return null;
        }
    }
}
#endif
