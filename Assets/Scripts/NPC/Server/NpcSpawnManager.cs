using System.Collections.Generic;
using Mirror;
using Sim.Logging;
using Sim.NPC;
using UnityEngine;

/// <summary>
/// Gère le cycle de vie des NPC : sélection des spawn points disponibles,
/// spawn via NpcPool (pas d'Instantiate/Destroy runtime), despawn propre,
/// respawn différé. Tourne en singleton plain C#.
///
/// Lifecycle :
///   - Spawn point libre + sous le cap → NpcPool.Get() → NPC actif
///   - NPC.RequestDespawn (ex. retour à la maison) → InternalRelease + NpcPool.Release()
///   - Le slot est verrouillé pendant RespawnDelaySeconds avant réutilisation.
/// </summary>
public class NpcSpawnManager {
    private static NpcSpawnManager _instance;
    public static  NpcSpawnManager Instance => _instance ??= new NpcSpawnManager();

    // ── Config ────────────────────────────────────────────────────────────────
    public int    MaxActiveNpcs       = 10;
    public float  RespawnDelaySeconds = 20f;
    public string RoomId              = "city";

    /// <summary>Database de noms (chargée par le bootstrap).</summary>
    public NpcNameDatabase NameDatabase;

    // ── État interne ──────────────────────────────────────────────────────────
    private readonly List<NpcSpawnPoint>   _spawnPoints  = new List<NpcSpawnPoint>();
    private readonly List<NpcAIController> _activeNpcs   = new List<NpcAIController>();
    private readonly Dictionary<NpcSpawnPoint, float> _cooldowns
        = new Dictionary<NpcSpawnPoint, float>();
    private readonly HashSet<string> _activeFullNames = new HashSet<string>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Retourne tous les NPC actifs au pool et réinitialise le tracking.
    /// Appelé depuis NpcSystemBootstrap.OnServerStop (avant NpcPool.Dispose).
    /// </summary>
    public void Reset() {
        // Copie la liste pour éviter la modification pendant l'itération.
        var toRelease = new List<NpcAIController>(_activeNpcs);

        // Efface le tracking en amont pour que OnNpcDestroyed (appelé via OnDisable)
        // ne tente pas de modifier des collections en cours d'itération.
        _activeNpcs.Clear();
        _cooldowns.Clear();
        _activeFullNames.Clear();

        foreach (var npc in toRelease) {
            if (npc == null) continue;
            // Libère le spawn point avant le Release pour éviter toute réutilisation
            // pendant le shutdown (NpcPool.Release appelle OnDisable).
            if (npc.Home != null) npc.Home.IsOccupied = false;
            NpcPool.Instance.Release(npc);
        }
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

    // ── Tick ──────────────────────────────────────────────────────────────────

    public void Tick(float deltaTime) {
        if (!NetworkServer.active) return;

        // Skip spawning entièrement quand la room n'a pas de joueurs.
        if (!RoomActivityController.Instance.IsRoomActive(RoomId)) return;

        // Décrémenter cooldowns
        if (_cooldowns.Count > 0) {
            var keys = new List<NpcSpawnPoint>(_cooldowns.Keys);
            foreach (var k in keys) {
                _cooldowns[k] -= deltaTime;
                if (_cooldowns[k] <= 0f) _cooldowns.Remove(k);
            }
        }

        if (_activeNpcs.Count >= MaxActiveNpcs) return;

        for (int i = 0; i < _spawnPoints.Count; i++) {
            var sp = _spawnPoints[i];
            if (sp == null || sp.IsOccupied) continue;
            if (_cooldowns.ContainsKey(sp))   continue;

            SpawnAt(sp);
            // Un seul spawn par tick pour amortir les pics de charge.
            return;
        }
    }

    // ── Spawn / despawn ───────────────────────────────────────────────────────

    private void SpawnAt(NpcSpawnPoint point) {
        // Config résolue ici (avec fallback « default ») : elle porte le prefab serveur à instancier
        // ET sera injectée telle quelle dans le NPC via ConfigureForSpawn (source unique).
        NpcConfig cfg = point.NpcConfig != null ? point.NpcConfig : Sim.DatabaseManager.DefaultNpcConfig;
        GameObject prefab = cfg != null ? cfg.ServerPrefab : null;
        if (prefab == null) {
            GameLogger.Network.Warning("NpcSpawnNoServerPrefab {SpawnPoint}", point.name);
            return;
        }

        NpcIdentity identity = GenerateUniqueIdentity();

        // NpcPool.Get() appelle ConfigureForSpawn + ResetForPool + SetActive(true) → OnEnable.
        NpcAIController ai = NpcPool.Instance.Get(
            prefab, point, identity, RoomId, cfg,
            point.Position, point.Rotation);

        if (ai == null) {
            GameLogger.Network.Error(null, "NpcSpawnPoolGetFailed {SpawnPoint}", point.name);
            return;
        }

        _activeFullNames.Add(identity.FullName);
        _activeNpcs.Add(ai);
        point.IsOccupied = true;

        GameLogger.Network.Info("NpcSpawnedAt {SpawnPoint} {FullName} {Active}/{Max}",
            point.name, identity.FullName, _activeNpcs.Count, MaxActiveNpcs);
    }

    /// <summary>Appelé par NpcBackToHomeState quand le NPC est rentré chez lui.</summary>
    public void RequestDespawn(NpcAIController npc) {
        if (npc == null) return;
        Despawn(npc);
    }

    /// <summary>
    /// Appelé par NpcAIController.OnDestroy (filet de sécurité — destruction inattendue).
    /// Lors d'un despawn normal via RequestDespawn, InternalRelease est déjà appelé.
    /// </summary>
    public void OnNpcDestroyed(NpcAIController npc) {
        if (npc == null) return;
        InternalRelease(npc);
    }

    private void Despawn(NpcAIController npc) {
        // Nettoie le tracking avant le Release pour éviter que OnDisable ne rappelle
        // InternalRelease depuis OnDestroy (NPC toujours actif pendant le Release).
        InternalRelease(npc);
        // NpcPool.Release → SetActive(false) → OnDisable → unregister seats/server.
        NpcPool.Instance.Release(npc);
        GameLogger.Network.Debug("[NPCPool] Returning NPC to pool (despawn) {FullName}",
            npc.Identity.FullName);
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
            Mood      = (Sim.Enums.MoodEnum)Random.Range(0, 5)
        };
    }
}
