using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using Sim.NPC;
using UnityEngine;

/// <summary>
/// Gère le cycle de vie des NPC : sélection des spawn points disponibles,
/// instanciation, despawn propre, respawn différé. Tourne en singleton plain C#
/// (cohérent avec NpcServerManager / ServerPropManager).
///
/// Lifecycle :
///   - Spawn point libre + sous le cap → instancier un NPC
///   - NPC.RequestDespawn (ex. retour à la maison) → Destroy + libérer le slot
///   - Le slot est verrouillé pendant <see cref="RespawnDelaySeconds"/> avant
///     de pouvoir être réutilisé.
///
/// Le tick (toutes les secondes par défaut) est piloté par un MonoBehaviour
/// helper créé par <see cref="NpcSystemBootstrap"/>.
/// </summary>
public class NpcSpawnManager {
    private static NpcSpawnManager _instance;
    public static  NpcSpawnManager Instance => _instance ??= new NpcSpawnManager();

    // ── Config (réglable depuis le bootstrap / scene component) ───────────────
    public int    MaxActiveNpcs       = 5;
    public float  RespawnDelaySeconds = 20f;
    public string RoomId              = "city";

    /// <summary>Prefab par défaut utilisé si un NpcSpawnPoint n'en spécifie pas.</summary>
    public GameObject DefaultPrefab;

    /// <summary>Database de noms (chargée par le bootstrap).</summary>
    public NpcNameDatabase NameDatabase;

    // ── État interne ──────────────────────────────────────────────────────────
    private readonly List<NpcSpawnPoint>  _spawnPoints       = new List<NpcSpawnPoint>();
    private readonly List<NpcAIController> _activeNpcs       = new List<NpcAIController>();
    private readonly Dictionary<NpcSpawnPoint, float> _cooldowns
        = new Dictionary<NpcSpawnPoint, float>();
    private readonly HashSet<string> _activeFullNames = new HashSet<string>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Reset() {
        // Détruit les NPC vivants. Ils s'auto-désenregistreront via OnDestroy.
        for (int i = _activeNpcs.Count - 1; i >= 0; i--) {
            if (_activeNpcs[i] != null) Object.Destroy(_activeNpcs[i].gameObject);
        }
        _activeNpcs.Clear();
        _cooldowns.Clear();
        _activeFullNames.Clear();
        // _spawnPoints est rempli par les OnEnable des NpcSpawnPoint en scène.
    }

    public void RegisterSpawnPoint(NpcSpawnPoint point) {
        if (point == null || _spawnPoints.Contains(point)) return;
        _spawnPoints.Add(point);
    }

    public void UnregisterSpawnPoint(NpcSpawnPoint point) {
        if (point == null) return;
        _spawnPoints.Remove(point);
        _cooldowns.Remove(point);
    }

    // ── Tick (sélection / spawn) ──────────────────────────────────────────────

    public void Tick(float deltaTime) {
        if (!NetworkServer.active) return;

        // Décrémenter cooldowns
        if (_cooldowns.Count > 0) {
            var keys = new List<NpcSpawnPoint>(_cooldowns.Keys);
            foreach (var k in keys) {
                _cooldowns[k] -= deltaTime;
                if (_cooldowns[k] <= 0f) _cooldowns.Remove(k);
            }
        }

        // Cap atteint ?
        if (_activeNpcs.Count >= MaxActiveNpcs) return;

        // Cherche un spawn point libre (non occupé, non en cooldown)
        for (int i = 0; i < _spawnPoints.Count; i++) {
            var sp = _spawnPoints[i];
            if (sp == null || sp.IsOccupied) continue;
            if (_cooldowns.ContainsKey(sp))   continue;

            SpawnAt(sp);
            // Un seul spawn par tick pour amortir les pics de charge
            return;
        }
    }

    // ── Spawn / despawn ───────────────────────────────────────────────────────

    private void SpawnAt(NpcSpawnPoint point) {
        GameObject prefab = point.NpcPrefab != null ? point.NpcPrefab : DefaultPrefab;
        if (prefab == null) {
            GameLogger.Network.Warning("NpcSpawnNoPrefab {SpawnPoint}", point.name);
            return;
        }

        GameObject go = Object.Instantiate(prefab, point.Position, point.Rotation);
        NpcAIController ai = go.GetComponent<NpcAIController>();
        if (ai == null) {
            GameLogger.Network.Error(null, "NpcSpawnNoAIController {Prefab}", prefab.name);
            Object.Destroy(go);
            return;
        }

        // Identité unique côté actifs.
        NpcIdentity identity = GenerateUniqueIdentity();
        _activeFullNames.Add(identity.FullName);

        // Configure l'IA AVANT son Start (instantiate déclenche Awake/Start
        // plus tard ce frame, après notre setter).
        ai.ConfigureForSpawn(point, identity, RoomId, point.PrefabId);

        _activeNpcs.Add(ai);
        point.IsOccupied = true;

        GameLogger.Network.Info("NpcSpawnedAt {SpawnPoint} {FullName} {Active}/{Max}",
            point.name, identity.FullName, _activeNpcs.Count, MaxActiveNpcs);
    }

    /// <summary>Appelé par <see cref="NpcBackToHomeState"/> quand le NPC est rentré.</summary>
    public void RequestDespawn(NpcAIController npc) {
        if (npc == null) return;
        Despawn(npc);
    }

    /// <summary>Appelé aussi par OnDestroy d'un NPC pour nettoyer le tracking.</summary>
    public void OnNpcDestroyed(NpcAIController npc) {
        if (npc == null) return;
        InternalRelease(npc);
    }

    private void Despawn(NpcAIController npc) {
        InternalRelease(npc);
        if (npc != null && npc.gameObject != null) Object.Destroy(npc.gameObject);
    }

    private void InternalRelease(NpcAIController npc) {
        _activeNpcs.Remove(npc);
        if (npc.Home != null) {
            npc.Home.IsOccupied = false;
            _cooldowns[npc.Home] = RespawnDelaySeconds;
        }
        if (!string.IsNullOrEmpty(npc.Identity.FullName))
            _activeFullNames.Remove(npc.Identity.FullName);
    }

    // ── Identité ──────────────────────────────────────────────────────────────

    private NpcIdentity GenerateUniqueIdentity() {
        if (NameDatabase == null) {
            return new NpcIdentity {
                FirstName = $"NPC{Random.Range(0, 9999)}",
                LastName  = "",
                Mood      = (Sim.Enums.MoodEnum)Random.Range(0, 5)
            };
        }

        const int MaxAttempts = 16;
        string first = "Anon", last = "";
        for (int i = 0; i < MaxAttempts; i++) {
            first = NameDatabase.PickRandomFirstName();
            last  = NameDatabase.PickRandomLastName();
            if (!_activeFullNames.Contains($"{first} {last}")) break;
        }

        return new NpcIdentity {
            FirstName = first,
            LastName  = last,
            // Mood random parmi les valeurs de MoodEnum (0..4 — cf. MoodEnum.cs)
            Mood      = (Sim.Enums.MoodEnum)Random.Range(0, 5)
        };
    }
}
