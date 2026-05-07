using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using Sim.NPC;
using UnityEngine;

/// <summary>
/// Côté client : reçoit les messages NPC, instancie les prefabs locaux,
/// route les snapshots vers ClientNpcView.
///
/// Filtrage par room :
///   - On ignore tout NPC dont RoomId ≠ ClientPropManager.Instance.CurrentRoomId
///     pour rester cohérent avec le routage room-based des autres systèmes.
///   - Le serveur ne broadcast déjà qu'aux conns de la room ; ce filtrage côté
///     client est une ceinture+bretelles utile pendant les transitions.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ClientNpcManager : MonoBehaviour {
    public static ClientNpcManager Instance { get; private set; }

    [Tooltip("Référence à la NpcPrefabDatabase (placée sous Resources si chargée par DatabaseManager).")]
    [SerializeField] private NpcPrefabDatabase prefabDatabase;

    [Tooltip("Chemin Resources (sans extension) si prefabDatabase n'est pas assigné.")]
    [SerializeField] private string prefabDatabaseResourcePath = "Configurations/Databases/NPC Database";

    private readonly Dictionary<int, ClientNpcView> _views = new Dictionary<int, ClientNpcView>();

    private void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (prefabDatabase == null && !string.IsNullOrEmpty(prefabDatabaseResourcePath)) {
            prefabDatabase = Resources.Load<NpcPrefabDatabase>(prefabDatabaseResourcePath);
        }
        if (prefabDatabase == null) {
            ClientLogger.NetworkWarning("ClientNpcManager: NpcPrefabDatabase not found at {Path}", prefabDatabaseResourcePath);
        }

        ClientLogger.Network("ClientNpcManagerInitialized");
    }

    public void RegisterHandlers() {
        NetworkClient.RegisterHandler<S2C_SpawnNpc>          (OnSpawnNpc);
        NetworkClient.RegisterHandler<S2C_UpdateNpcTransform>(OnUpdateTransform);
        NetworkClient.RegisterHandler<S2C_DestroyNpc>        (OnDestroyNpc);
        ClientLogger.NetworkDebug("ClientNpcHandlersRegistered");
    }

    public void UnregisterHandlers() {
        NetworkClient.UnregisterHandler<S2C_SpawnNpc>();
        NetworkClient.UnregisterHandler<S2C_UpdateNpcTransform>();
        NetworkClient.UnregisterHandler<S2C_DestroyNpc>();
        ClientLogger.NetworkDebug("ClientNpcHandlersUnregistered");
    }

    public void ClearAll() {
        foreach (var v in _views.Values) {
            if (v != null) Destroy(v.gameObject);
        }
        _views.Clear();
    }

    // ── Mirror handlers ───────────────────────────────────────────────────────

    private void OnSpawnNpc(S2C_SpawnNpc msg) {
        if (_views.ContainsKey(msg.NpcId)) {
            ClientLogger.NetworkDebug("NpcSpawnDuplicate {NpcId}", msg.NpcId);
            return;
        }
        if (prefabDatabase == null) {
            ClientLogger.NetworkWarning("NpcSpawnNoDatabase {NpcId} {PrefabId}", msg.NpcId, msg.PrefabId);
            return;
        }
        GameObject prefab = prefabDatabase.GetPrefab(msg.PrefabId);
        if (prefab == null) {
            ClientLogger.NetworkWarning("NpcSpawnPrefabNotFound {NpcId} {PrefabId}", msg.NpcId, msg.PrefabId);
            return;
        }

        GameObject go = Instantiate(prefab, msg.Position, msg.Rotation);
        string fullName = string.IsNullOrEmpty(msg.LastName) ? msg.FirstName : $"{msg.FirstName} {msg.LastName}";
        go.name = $"NPC#{msg.NpcId} {fullName}";

        ClientNpcView view = go.GetComponent<ClientNpcView>();
        if (view == null) view = go.AddComponent<ClientNpcView>();
        view.Init(msg.NpcId, msg.RoomId, msg.StyleJson, msg.FirstName, msg.LastName, msg.Mood);

        _views[msg.NpcId] = view;
        ClientLogger.NetworkDebug("NpcSpawned {NpcId} {PrefabId} {RoomId}", msg.NpcId, msg.PrefabId, msg.RoomId);
    }

    private void OnUpdateTransform(S2C_UpdateNpcTransform msg) {
        if (_views.TryGetValue(msg.NpcId, out var view) && view != null) {
            view.PushSnapshot(msg.Position, msg.Rotation, msg.Velocity, msg.AnimationState);
        }
    }

    private void OnDestroyNpc(S2C_DestroyNpc msg) {
        if (_views.TryGetValue(msg.NpcId, out var view)) {
            if (view != null) Destroy(view.gameObject);
            _views.Remove(msg.NpcId);
            ClientLogger.NetworkDebug("NpcDestroyed {NpcId} {RoomId}", msg.NpcId, msg.RoomId);
        }
    }
}
