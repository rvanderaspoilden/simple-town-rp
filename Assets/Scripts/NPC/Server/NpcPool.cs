using System.Collections.Generic;
using Sim.Logging;
using UnityEngine;

/// <summary>
/// Server-side object pool for NPC GameObjects, keyed by prefab.
///
/// Avoids <c>Instantiate</c>/<c>Destroy</c> churn when NPCs spawn/despawn.
/// Pool entries are kept deactivated; activation drives the NPC lifecycle
/// (registration with <see cref="NpcServerManager"/>, state-machine reset,
/// style randomization). Deactivation runs the reverse path.
///
/// Usage contract:
///   <code>
///     NpcAIController npc = NpcPool.Instance.Get(
///         prefab, spawnPoint, identity, roomId, prefabId, position, rotation);
///     // ... NPC lives ...
///     NpcPool.Instance.Release(npc);
///   </code>
///
/// The lifecycle logic lives in <see cref="NpcAIController.OnEnable"/> /
/// <see cref="NpcAIController.OnDisable"/> — the pool is only responsible
/// for container reuse. This keeps the NPC agnostic of whether it was
/// freshly instantiated or dequeued.
///
/// Plain C# singleton. No MonoBehaviour — pooled GOs are held as inactive
/// scene objects under a dedicated container so they survive scene reloads
/// alongside <c>DontDestroyOnLoad</c>.
/// </summary>
public class NpcPool {
    private static NpcPool _instance;
    public static NpcPool Instance => _instance ??= new NpcPool();

    // prefab → queue of inactive NpcAIController instances ready for reuse
    private readonly Dictionary<GameObject, Queue<NpcAIController>> _available
        = new Dictionary<GameObject, Queue<NpcAIController>>();

    // Every controller we have ever produced — used to destroy all on Dispose.
    private readonly List<NpcAIController> _allTracked = new List<NpcAIController>();

    private Transform _container;

    private Transform Container {
        get {
            if (_container == null) {
                var go = new GameObject("NpcPool");
                Object.DontDestroyOnLoad(go);
                _container = go.transform;
            }
            return _container;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves an NPC for the given prefab, configures it for spawn and
    /// activates it. Returns null if the prefab is missing a
    /// <see cref="NpcAIController"/>.
    /// </summary>
    public NpcAIController Get(GameObject prefab, NpcSpawnPoint home, NpcIdentity identity,
                                string roomId, string prefabId,
                                Vector3 position, Quaternion rotation) {
        if (prefab == null) {
            GameLogger.Network.Warning("NpcPoolGetNullPrefab");
            return null;
        }

        NpcAIController ai = TakeFromQueue(prefab);
        bool reused = ai != null;

        if (!reused) {
            GameObject go = Object.Instantiate(prefab, position, rotation, Container);
            go.SetActive(false);   // configure before OnEnable fires
            ai = go.GetComponent<NpcAIController>();
            if (ai == null) {
                GameLogger.Network.Error(null, "NpcPoolPrefabNoAIController {Prefab}", prefab.name);
                Object.Destroy(go);
                return null;
            }
            ai.SourcePrefab = prefab;
            _allTracked.Add(ai);
        }
        else {
            ai.transform.SetPositionAndRotation(position, rotation);
        }

        // Configure spawn data before the NPC's OnEnable registers it with the
        // NpcServerManager (needs final roomId / prefabId / identity).
        ai.ConfigureForSpawn(home, identity, roomId, prefabId);
        ai.ResetForPool();

        ai.gameObject.SetActive(true);  // triggers OnEnable → Register

        GameLogger.Network.Info(reused
            ? "NpcPoolReused {Prefab} {Remaining}"
            : "NpcPoolCreated {Prefab} {Remaining}",
            prefab.name, AvailableCount(prefab));

        return ai;
    }

    /// <summary>
    /// Deactivates the NPC (triggering unregistration via <see cref="NpcAIController.OnDisable"/>)
    /// and returns the GameObject to the pool queue for later reuse.
    /// </summary>
    public void Release(NpcAIController ai) {
        if (ai == null || ai.gameObject == null) return;

        GameObject prefab = ai.SourcePrefab;
        if (prefab == null) {
            // Untracked — just destroy.
            GameLogger.Network.Warning("NpcPoolReleaseUntracked, destroying");
            Object.Destroy(ai.gameObject);
            return;
        }

        ai.gameObject.SetActive(false);           // OnDisable → Unregister + release seats
        ai.transform.SetParent(Container, false); // keep hierarchy tidy

        if (!_available.TryGetValue(prefab, out var queue)) {
            queue = new Queue<NpcAIController>();
            _available[prefab] = queue;
        }
        queue.Enqueue(ai);

        GameLogger.Network.Debug("NpcPoolReleased {Prefab} {Pooled}", prefab.name, queue.Count);
    }

    /// <summary>
    /// Pre-instantiates <paramref name="count"/> NPCs for <paramref name="prefab"/>
    /// and stores them inactive in the pool. Call at boot to avoid first-spawn hitches.
    /// </summary>
    public void Warmup(GameObject prefab, int count) {
        if (prefab == null || count <= 0) return;

        for (int i = 0; i < count; i++) {
            GameObject go = Object.Instantiate(prefab, Container);
            go.SetActive(false);
            NpcAIController ai = go.GetComponent<NpcAIController>();
            if (ai == null) {
                Object.Destroy(go);
                continue;
            }
            ai.SourcePrefab = prefab;
            _allTracked.Add(ai);

            if (!_available.TryGetValue(prefab, out var queue)) {
                queue = new Queue<NpcAIController>();
                _available[prefab] = queue;
            }
            queue.Enqueue(ai);
        }
        GameLogger.Network.Info("NpcPoolWarmup {Prefab} {Count}", prefab.name, count);
    }

    /// <summary>Destroys every NPC GO (pooled and active). Called on server stop.</summary>
    public void Dispose() {
        for (int i = _allTracked.Count - 1; i >= 0; i--) {
            if (_allTracked[i] != null && _allTracked[i].gameObject != null) {
                Object.Destroy(_allTracked[i].gameObject);
            }
        }
        _allTracked.Clear();
        _available.Clear();
        if (_container != null) {
            Object.Destroy(_container.gameObject);
            _container = null;
        }
    }

    public int AvailableCount(GameObject prefab) =>
        _available.TryGetValue(prefab, out var q) ? q.Count : 0;

    // ── Internals ─────────────────────────────────────────────────────────────

    private NpcAIController TakeFromQueue(GameObject prefab) {
        if (!_available.TryGetValue(prefab, out var queue)) return null;
        while (queue.Count > 0) {
            var ai = queue.Dequeue();
            // Guard against destroyed-but-still-queued entries (scene reload etc.)
            if (ai != null && ai.gameObject != null) return ai;
        }
        return null;
    }
}
