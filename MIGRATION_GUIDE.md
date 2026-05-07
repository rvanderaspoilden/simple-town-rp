# 🔄 Guide de Migration vers le Logging Structuré

Ce guide montre comment migrer votre code existant de `Debug.Log` vers `GameLogger`.

---

## 📋 Exemples de Migration

### 1. ServerPropManager - Spawn de Props

#### ❌ Avant
```csharp
public int SpawnProp(string roomId, int prefabId, Vector3 position, Quaternion rotation, PropType type, byte[] payload = null) {
    Debug.Log($"Spawning prop {prefabId} in room {roomId} at {position}");
    
    try {
        var propId = _nextAutoId++;
        var prefab = GetPrefab(prefabId);
        
        if (prefab == null) {
            Debug.LogError($"Prefab {prefabId} not found!");
            return -1;
        }
        
        var go = UnityEngine.Object.Instantiate(prefab, position, rotation);
        _spawnedGOs[propId] = go;
        
        RegisterProp(roomId, propId, prefabId, position, rotation, type, payload);
        BroadcastSpawn(roomId, propId, prefabId, position, rotation, type, payload);
        
        Debug.Log($"Prop {propId} spawned successfully");
        return propId;
    }
    catch (Exception ex) {
        Debug.LogError($"Error spawning prop: {ex.Message}");
        return -1;
    }
}
```

#### ✅ Après
```csharp
using Sim.Logging;

public int SpawnProp(string roomId, int prefabId, Vector3 position, Quaternion rotation, PropType type, byte[] payload = null) {
    GameLogger.Props.Debug("SpawnPropRequested {PrefabId} {RoomId} Position={Position}", 
        prefabId, roomId, position);
    
    try {
        var propId = _nextAutoId++;
        var prefab = GetPrefab(prefabId);
        
        if (prefab == null) {
            GameLogger.Props.Error("PrefabNotFound {PrefabId} {RoomId}", prefabId, roomId);
            return -1;
        }
        
        var go = UnityEngine.Object.Instantiate(prefab, position, rotation);
        _spawnedGOs[propId] = go;
        
        RegisterProp(roomId, propId, prefabId, position, rotation, type, payload);
        BroadcastSpawn(roomId, propId, prefabId, position, rotation, type, payload);
        
        GameLogger.Props.Info("PropSpawned {PropId} {RoomId} {PrefabId} {Type} Position={Position}", 
            propId, roomId, prefabId, type, position);
        return propId;
    }
    catch (Exception ex) {
        GameLogger.Props.Error(ex, "PropSpawnFailed {PrefabId} {RoomId}", prefabId, roomId);
        return -1;
    }
}
```

---

### 2. ServerPropManager - Update Transform

#### ❌ Avant
```csharp
public void UpdatePropTransform(string roomId, int propId, Vector3 position, Quaternion rotation) {
    Debug.Log($"Updating prop {propId} transform in room {roomId}");
    
    if (!TryGetState(roomId, propId, out var state)) {
        Debug.LogWarning($"Prop {propId} not found in room {roomId}");
        return;
    }
    
    state.Position = position;
    state.Rotation = rotation;
    
    BroadcastToRoom(roomId, new S2C_PropTransform {
        PropId = propId,
        Position = position,
        Rotation = rotation
    });
}
```

#### ✅ Après
```csharp
using Sim.Logging;

public void UpdatePropTransform(string roomId, int propId, Vector3 position, Quaternion rotation) {
    if (GameLogger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
        GameLogger.Props.Debug("UpdatePropTransformRequested {PropId} {RoomId} Position={Position}", 
            propId, roomId, position);
    }
    
    if (!TryGetState(roomId, propId, out var state)) {
        GameLogger.Props.Warning("PropNotFoundForTransformUpdate {PropId} {RoomId}", propId, roomId);
        return;
    }
    
    state.Position = position;
    state.Rotation = rotation;
    
    BroadcastToRoom(roomId, new S2C_PropTransform {
        PropId = propId,
        Position = position,
        Rotation = rotation
    });
    
    if (GameLogger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
        GameLogger.Props.Debug("PropTransformUpdated {PropId} {RoomId}", propId, roomId);
    }
}
```

---

### 3. Network - Client Connection

#### ❌ Avant
```csharp
void OnServerConnect(NetworkConnectionToClient conn) {
    Debug.Log($"Client connected: {conn.connectionId} from {conn.address}");
}

void OnServerDisconnect(NetworkConnectionToClient conn) {
    Debug.Log($"Client disconnected: {conn.connectionId}");
}
```

#### ✅ Après
```csharp
using Sim.Logging;

void OnServerConnect(NetworkConnectionToClient conn) {
    GameLogger.Network.Info("ClientConnected {ConnectionId} {Address}", 
        conn.connectionId, conn.address);
}

void OnServerDisconnect(NetworkConnectionToClient conn) {
    GameLogger.Network.Info("ClientDisconnected {ConnectionId} {Address}", 
        conn.connectionId, conn.address);
}
```

---

### 4. Room Management - Player Enter/Leave

#### ❌ Avant
```csharp
public void OnPlayerEnterRoom(string playerId, string roomId) {
    Debug.Log($"Player {playerId} entered room {roomId}");
    
    var playerCount = GetPlayerCount(roomId);
    Debug.Log($"Room {roomId} now has {playerCount} players");
    
    SendRoomSnapshot(playerId, roomId);
}

public void OnPlayerLeaveRoom(string playerId, string roomId) {
    var duration = GetPlayerDuration(playerId, roomId);
    Debug.Log($"Player {playerId} left room {roomId} after {duration} seconds");
}
```

#### ✅ Après
```csharp
using Sim.Logging;

public void OnPlayerEnterRoom(string playerId, string roomId) {
    var playerCount = GetPlayerCount(roomId);
    
    GameLogger.Rooms.Info("PlayerEnteredRoom {PlayerId} {RoomId} {PlayerCount}", 
        playerId, roomId, playerCount);
    
    SendRoomSnapshot(playerId, roomId);
}

public void OnPlayerLeaveRoom(string playerId, string roomId) {
    var duration = GetPlayerDuration(playerId, roomId);
    
    GameLogger.Rooms.Info("PlayerLeftRoom {PlayerId} {RoomId} {DurationSeconds}", 
        playerId, roomId, duration);
}
```

---

### 5. DeliveryBox Behaviour

#### ❌ Avant
```csharp
public void OnDeliveryBoxOpened(string playerId) {
    Debug.Log($"Player {playerId} opened delivery box {propId}");
    
    if (playerId != ownerId) {
        Debug.LogWarning($"Unauthorized access: {playerId} tried to open box owned by {ownerId}");
        return;
    }
    
    Debug.Log($"Delivery box {propId} contents delivered to {playerId}");
}
```

#### ✅ Après
```csharp
using Sim.Logging;

public void OnDeliveryBoxOpened(string playerId) {
    GameLogger.Props.Info("DeliveryBoxOpenAttempt {PropId} {PlayerId} {OwnerId}", 
        propId, playerId, ownerId);
    
    if (playerId != ownerId) {
        GameLogger.Props.Warning("DeliveryBoxUnauthorizedAccess {PropId} {PlayerId} {OwnerId}", 
            propId, playerId, ownerId);
        return;
    }
    
    GameLogger.Props.Info("DeliveryBoxOpened {PropId} {PlayerId}", propId, playerId);
}
```

---

### 6. Error Handling avec Try/Catch

#### ❌ Avant
```csharp
try {
    ProcessPropUpdate(propId, data);
}
catch (Exception ex) {
    Debug.LogError($"Error processing prop {propId}: {ex.Message}");
    Debug.LogException(ex);
}
```

#### ✅ Après
```csharp
using Sim.Logging;

try {
    ProcessPropUpdate(propId, data);
}
catch (Exception ex) {
    GameLogger.Props.Error(ex, "PropUpdateFailed {PropId} {RoomId}", propId, roomId);
}
```

---

### 7. Performance Monitoring

#### ❌ Avant
```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
ProcessHeavyOperation();
sw.Stop();

if (sw.ElapsedMilliseconds > 100) {
    Debug.LogWarning($"Slow operation: {sw.ElapsedMilliseconds}ms");
}
```

#### ✅ Après (Option 1 - Manuel)
```csharp
using Sim.Logging;

var sw = System.Diagnostics.Stopwatch.StartNew();
ProcessHeavyOperation();
sw.Stop();

if (sw.ElapsedMilliseconds > 100) {
    GameLogger.System.Warning("SlowOperation {Operation} {DurationMs} {ThresholdMs}", 
        "ProcessHeavyOperation", sw.ElapsedMilliseconds, 100);
}
```

#### ✅ Après (Option 2 - Automatique avec using)
```csharp
using Sim.Logging;

using (IntegrationExamples.PerformanceMonitoring.MeasureOperation("ProcessHeavyOperation", 100f)) {
    ProcessHeavyOperation();
}
```

---

### 8. Database Operations

#### ❌ Avant
```csharp
public void SavePlayerData(string playerId, PlayerData data) {
    Debug.Log($"Saving player data for {playerId}");
    
    try {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        database.Save(playerId, data);
        sw.Stop();
        
        Debug.Log($"Player data saved in {sw.ElapsedMilliseconds}ms");
    }
    catch (Exception ex) {
        Debug.LogError($"Failed to save player data: {ex.Message}");
    }
}
```

#### ✅ Après
```csharp
using Sim.Logging;

public void SavePlayerData(string playerId, PlayerData data) {
    GameLogger.System.Debug("SavePlayerDataRequested {PlayerId}", playerId);
    
    try {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        database.Save(playerId, data);
        sw.Stop();
        
        GameLogger.System.Debug("DatabaseOperation {Operation} {Collection} {DurationMs}", 
            "Save", "PlayerData", sw.ElapsedMilliseconds);
    }
    catch (Exception ex) {
        GameLogger.System.Error(ex, "DatabaseOperationFailed {Operation} {PlayerId}", 
            "Save", playerId);
    }
}
```

---

### 9. Server Startup/Shutdown

#### ❌ Avant
```csharp
void Start() {
    Debug.Log("Server starting...");
    Debug.Log($"Version: {Application.version}");
    Debug.Log($"Build: {Application.buildGUID}");
    
    InitializeServer();
    
    Debug.Log("Server started successfully");
}

void OnApplicationQuit() {
    Debug.Log("Server shutting down...");
    var activeConnections = NetworkServer.connections.Count;
    Debug.Log($"Active connections: {activeConnections}");
}
```

#### ✅ Après
```csharp
using Sim.Logging;

void Start() {
    GameLogger.System.Info("ServerStartup {Version} {BuildGUID} {IsHeadless}", 
        Application.version, Application.buildGUID, Application.isBatchMode);
    
    InitializeServer();
    
    GameLogger.System.Info("ServerStartupComplete");
}

void OnApplicationQuit() {
    var activeConnections = NetworkServer.connections.Count;
    var totalRooms = GetTotalRoomCount();
    
    GameLogger.System.Info("ServerShutdown {ActiveConnections} {TotalRooms}", 
        activeConnections, totalRooms);
}
```

---

### 10. Conditional Debug Logging (Performance)

#### ❌ Avant
```csharp
#if UNITY_EDITOR
Debug.Log($"Detailed debug info: {expensiveOperation()}");
#endif
```

#### ✅ Après
```csharp
using Sim.Logging;

if (GameLogger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
    var debugInfo = expensiveOperation();
    GameLogger.Debug("DetailedDebugInfo {Info}", debugInfo);
}
```

---

## 🎯 Patterns de Migration Rapide

### Pattern 1: Simple Info Log
```csharp
// Avant
Debug.Log($"Action {action} by {playerId}");

// Après
GameLogger.Info("Action {Action} {PlayerId}", action, playerId);
```

### Pattern 2: Warning
```csharp
// Avant
Debug.LogWarning($"Warning: {message}");

// Après
GameLogger.Warning("Warning {Message}", message);
```

### Pattern 3: Error avec Exception
```csharp
// Avant
Debug.LogError($"Error: {ex.Message}");
Debug.LogException(ex);

// Après
GameLogger.Error(ex, "Error in {Context}", context);
```

### Pattern 4: Debug avec Condition
```csharp
// Avant
if (debugMode) {
    Debug.Log($"Debug: {info}");
}

// Après
if (GameLogger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
    GameLogger.Debug("Debug {Info}", info);
}
```

---

## 📝 Checklist de Migration par Fichier

Pour chaque fichier serveur :

- [ ] Ajouter `using Sim.Logging;` en haut du fichier
- [ ] Remplacer `Debug.Log(...)` par `GameLogger.*.Info(...)`
- [ ] Remplacer `Debug.LogWarning(...)` par `GameLogger.*.Warning(...)`
- [ ] Remplacer `Debug.LogError(...)` par `GameLogger.*.Error(...)`
- [ ] Remplacer string interpolation `$"..."` par message templates `"... {Param}"`
- [ ] Ajouter contexte (IDs) dans les logs
- [ ] Choisir la bonne catégorie (Network, Props, Rooms, Player, System)
- [ ] Tester que les logs sont générés correctement

---

## 🔍 Recherche & Remplacement (Regex)

### Trouver les Debug.Log avec interpolation
```regex
Debug\.Log\(\$"([^"]+)"\)
```

### Trouver les Debug.LogError
```regex
Debug\.LogError\([^)]+\)
```

### Trouver les Debug.LogWarning
```regex
Debug\.LogWarning\([^)]+\)
```

---

## ⚠️ Points d'Attention

1. **Ne pas migrer les logs client** - Seulement le code serveur
2. **Garder Debug.Log en fallback** - Si le logger n'est pas initialisé
3. **Vérifier les performances** - Utiliser `IsEnabled()` pour logs coûteux
4. **Tester en local** - Avant de déployer en production
5. **Monitorer Seq** - Vérifier que les logs arrivent correctement

---

## 🚀 Ordre de Migration Recommandé

1. **ServerPropManager** (haute priorité - beaucoup de logs)
2. **Network handlers** (connexions, messages)
3. **Room management** (entrées/sorties)
4. **Player actions** (interactions)
5. **System/Database** (opérations critiques)
6. **Behaviours** (DeliveryBox, etc.)

---

## ✅ Validation Post-Migration

Après migration, vérifier :

- [ ] Les logs JSON sont créés dans `/Logs/`
- [ ] Les logs contiennent les propriétés structurées
- [ ] Seq affiche les logs (si configuré)
- [ ] Pas de régression fonctionnelle
- [ ] Performance acceptable (< 1% overhead)
- [ ] Logs lisibles et exploitables
