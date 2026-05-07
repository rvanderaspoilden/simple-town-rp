# 📋 Guide de Configuration du Logging Structuré

## 🎯 Vue d'ensemble

Système de logging structuré basé sur **Serilog** pour le serveur Unity/Mirror.

### ✅ Fonctionnalités

- **Logs structurés** en JSON (parsables)
- **Rolling logs** quotidiens avec rétention de 30 jours
- **Catégories** dédiées (Network, Props, Rooms, Player, System)
- **Niveaux configurables** (Debug, Info, Warning, Error, Fatal)
- **Support Seq** pour visualisation en temps réel
- **Performance optimisée** (buffering, async)

---

## 📦 Installation des Packages NuGet

### Packages requis

Ajoutez ces packages via NuGet for Unity ou manuellement :

```
Serilog (v3.1.1 ou supérieur)
Serilog.Sinks.File (v5.0.0 ou supérieur)
Serilog.Formatting.Compact (v2.0.0 ou supérieur)
Serilog.Enrichers.Thread (v3.1.0 ou supérieur)
Serilog.Sinks.Seq (v6.0.0 ou supérieur) [optionnel]
```

### Installation via NuGet for Unity

1. Installer NuGet for Unity depuis l'Asset Store
2. Ouvrir `NuGet > Manage NuGet Packages`
3. Rechercher et installer les packages ci-dessus

### Installation manuelle

Téléchargez les DLLs depuis nuget.org et placez-les dans `Assets/Plugins/`

---

## 🚀 Initialisation

### Méthode 1 : Via MonoBehaviour (Recommandé)

1. Créez un GameObject vide dans votre scène serveur
2. Nommez-le `ServerLogger`
3. Attachez le composant `ServerLoggerInitializer`
4. Configurez les options :
   - `Initialize On Awake` : ✅ (coché)
   - `Server Only` : ✅ (coché)

### Méthode 2 : Initialisation manuelle

```csharp
using Sim.Logging;

public class ServerBootstrap : MonoBehaviour {
    private void Awake() {
        if (NetworkServer.active) {
            LoggerBootstrap.Initialize();
        }
    }

    private void OnApplicationQuit() {
        LoggerBootstrap.Shutdown();
    }
}
```

---

## 📝 Utilisation

### Syntaxe de base

**❌ À NE PAS FAIRE :**
```csharp
Debug.Log($"Player {playerId} entered room {roomId}");
Debug.Log("Prop spawned: " + propId);
```

**✅ À FAIRE :**
```csharp
GameLogger.Rooms.Info("PlayerEnteredRoom {PlayerId} {RoomId}", playerId, roomId);
GameLogger.Props.Info("PropSpawned {PropId} {RoomId} {Type}", propId, roomId, type);
```

### Catégories disponibles

#### 1. Network
```csharp
GameLogger.Network.Info("ClientConnected {ConnectionId} {IpAddress}", connId, ip);
GameLogger.Network.Debug("MessageReceived {MessageType} {Size}", msgType, size);
GameLogger.Network.Error(ex, "Connection failed for {ConnectionId}", connId);
```

#### 2. Props
```csharp
GameLogger.Props.Info("PropSpawned {PropId} {RoomId} {PrefabId}", propId, roomId, prefabId);
GameLogger.Props.Debug("PropUpdated {PropId} Position={Position}", propId, position);
GameLogger.Props.Warning("PropLimitReached {RoomId} {Count}", roomId, count);
GameLogger.Props.Error(ex, "Failed to spawn prop {PropId}", propId);
```

#### 3. Rooms
```csharp
GameLogger.Rooms.Info("RoomCreated {RoomId} {OwnerId}", roomId, ownerId);
GameLogger.Rooms.Info("PlayerEnteredRoom {PlayerId} {RoomId}", playerId, roomId);
GameLogger.Rooms.Info("PlayerLeftRoom {PlayerId} {RoomId} {Duration}", playerId, roomId, duration);
```

#### 4. Player
```csharp
GameLogger.Player.Info("PlayerAction {PlayerId} {Action} {Target}", playerId, action, target);
GameLogger.Player.Debug("PlayerInventoryChanged {PlayerId} {ItemId} {Quantity}", playerId, itemId, qty);
```

#### 5. System
```csharp
GameLogger.System.Info("ServerStartup {Version} {BuildDate}", version, buildDate);
GameLogger.System.Warning("PerformanceWarning {Component} {ProcessingTimeMs}", component, time);
GameLogger.System.Error(ex, "Critical system error in {Component}", component);
```

#### 6. Logs génériques
```csharp
GameLogger.Info("Generic information message {Key}", value);
GameLogger.Warning("Warning message {Key}", value);
GameLogger.Error(ex, "Error message {Key}", value);
GameLogger.Fatal(ex, "Fatal error, server unstable");
```

---

## ⚙️ Configuration

### Variables d'environnement

Configurez via variables d'environnement Windows :

```batch
# Niveau de log minimum (Debug, Information, Warning, Error, Fatal)
SET LOG_LEVEL=Information

# Environnement (Development, Staging, Production)
SET ENVIRONMENT=Production

# Activation de Seq
SET SEQ_ENABLED=true
SET SEQ_URL=http://localhost:5341
```

### Configuration par défaut

Sans variables d'environnement :
- **Editor** : `Debug` level
- **Build** : `Information` level
- **Logs** : `Logs/log-YYYY-MM-DD.json`
- **Rétention** : 30 jours

---

## 📊 Format des logs JSON

Exemple de log généré :

```json
{
  "@t": "2026-05-07T10:29:15.1234567Z",
  "@mt": "PropSpawned {PropId} {RoomId} {Type}",
  "@l": "Information",
  "PropId": 42,
  "RoomId": "room_abc123",
  "Type": "DeliveryBox",
  "Category": "Props",
  "Application": "SimpleTownRP",
  "Environment": "Production",
  "MachineName": "SERVER-01",
  "ThreadId": 12
}
```

### Champs automatiques

- `@t` : Timestamp ISO 8601
- `@mt` : Message template
- `@l` : Log level
- `@x` : Exception (si présente)
- `Category` : Catégorie du log
- `Application` : Nom de l'application
- `Environment` : Environnement (Dev/Prod)
- `MachineName` : Nom de la machine
- `ThreadId` : ID du thread

---

## 🔍 Visualisation avec Seq

### Installation de Seq (Windows)

1. Téléchargez Seq : https://datalust.co/download
2. Installez Seq (service Windows)
3. Accédez à http://localhost:5341

### Configuration

```batch
SET SEQ_ENABLED=true
SET SEQ_URL=http://localhost:5341
```

### Requêtes Seq utiles

```sql
-- Tous les spawns de props
Category = "Props" AND @mt LIKE "%PropSpawned%"

-- Erreurs dans une room spécifique
RoomId = "room_abc123" AND @l = "Error"

-- Performance warnings
@mt LIKE "%PerformanceWarning%"

-- Activité d'un joueur
PlayerId = "player_123"

-- Erreurs réseau
Category = "Network" AND @l IN ["Error", "Fatal"]
```

---

## 🎯 Exemples d'intégration

### Exemple 1 : ServerPropManager

**Avant :**
```csharp
Debug.Log($"Spawning prop {propId} in room {roomId}");
```

**Après :**
```csharp
GameLogger.Props.Info("PropSpawned {PropId} {RoomId} {PrefabId} {Type}", 
    propId, roomId, prefabId, type);
```

### Exemple 2 : Gestion d'erreur

**Avant :**
```csharp
try {
    UpdateProp(propId);
} catch (Exception ex) {
    Debug.LogError($"Error updating prop {propId}: {ex.Message}");
}
```

**Après :**
```csharp
try {
    UpdateProp(propId);
} catch (Exception ex) {
    GameLogger.Props.Error(ex, "Failed to update prop {PropId} in room {RoomId}", 
        propId, roomId);
}
```

### Exemple 3 : Logs de performance

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
ProcessHeavyOperation();
sw.Stop();

if (sw.ElapsedMilliseconds > 100) {
    GameLogger.System.Warning("SlowOperation {Operation} {DurationMs} {Threshold}", 
        "ProcessHeavyOperation", sw.ElapsedMilliseconds, 100);
}
```

---

## 🚨 Bonnes pratiques

### ✅ DO

1. **Utilisez des message templates** avec placeholders nommés
   ```csharp
   GameLogger.Props.Info("PropSpawned {PropId} {RoomId}", propId, roomId);
   ```

2. **Loggez les IDs importants** (PlayerId, RoomId, PropId, ConnectionId)
   ```csharp
   GameLogger.Rooms.Info("PlayerEnteredRoom {PlayerId} {RoomId}", playerId, roomId);
   ```

3. **Incluez le contexte** dans les erreurs
   ```csharp
   GameLogger.Props.Error(ex, "Failed to spawn prop {PropId} in room {RoomId}", propId, roomId);
   ```

4. **Utilisez les bons niveaux**
   - `Debug` : Informations de développement (désactivé en prod)
   - `Info` : Événements importants (spawn, connexion, etc.)
   - `Warning` : Situations anormales mais gérées
   - `Error` : Erreurs nécessitant attention
   - `Fatal` : Erreurs critiques menaçant la stabilité

5. **Vérifiez le niveau avant logs coûteux**
   ```csharp
   if (GameLogger.IsEnabled(LogEventLevel.Debug)) {
       var expensiveData = ComputeExpensiveDebugInfo();
       GameLogger.Debug("DebugInfo {Data}", expensiveData);
   }
   ```

### ❌ DON'T

1. **N'utilisez PAS string interpolation**
   ```csharp
   // ❌ MAUVAIS
   GameLogger.Info($"Player {playerId} entered");
   
   // ✅ BON
   GameLogger.Info("PlayerEntered {PlayerId}", playerId);
   ```

2. **N'utilisez PAS de concaténation**
   ```csharp
   // ❌ MAUVAIS
   GameLogger.Info("Prop: " + propId);
   
   // ✅ BON
   GameLogger.Info("PropSpawned {PropId}", propId);
   ```

3. **Ne loggez PAS d'informations sensibles**
   ```csharp
   // ❌ MAUVAIS
   GameLogger.Info("UserPassword {Password}", password);
   ```

4. **N'abusez PAS du niveau Debug en production**
   - Utilisez `Information` pour les événements importants
   - Réservez `Debug` pour le développement

---

## 🔧 Dépannage

### Les logs ne sont pas créés

1. Vérifiez que `LoggerBootstrap.Initialize()` est appelé
2. Vérifiez les permissions d'écriture dans le dossier `Logs/`
3. Consultez la console Unity pour les erreurs d'initialisation

### Logs trop verbeux

```batch
SET LOG_LEVEL=Warning
```

### Seq ne reçoit pas les logs

1. Vérifiez que Seq est démarré : http://localhost:5341
2. Vérifiez les variables d'environnement :
   ```batch
   SET SEQ_ENABLED=true
   SET SEQ_URL=http://localhost:5341
   ```
3. Vérifiez le firewall Windows

### Performance impactée

1. Augmentez le niveau minimum : `Information` ou `Warning`
2. Réduisez la fréquence de flush (déjà optimisé à 5s)
3. Désactivez les logs `Debug` en production

---

## 📈 Performance

### Optimisations implémentées

- **Buffering** : Logs écrits par batch toutes les 5 secondes
- **Async** : Écriture non-bloquante
- **Message templates** : Évite allocations de strings
- **Structured logging** : Pas de parsing/formatting coûteux

### Impact mesuré

- **Overhead par log** : ~0.001ms (négligeable)
- **Mémoire** : ~50KB buffer
- **Disk I/O** : Flush toutes les 5s (non-bloquant)

---

## 📚 Ressources

- [Serilog Documentation](https://serilog.net/)
- [Seq Documentation](https://docs.datalust.co/docs)
- [Compact JSON Format](https://github.com/serilog/serilog-formatting-compact)

---

## ✅ Checklist de migration

- [ ] Installer les packages NuGet Serilog
- [ ] Ajouter `ServerLoggerInitializer` à la scène serveur
- [ ] Configurer les variables d'environnement
- [ ] Remplacer `Debug.Log` par `GameLogger.*` dans le code serveur
- [ ] Tester en local avec Seq
- [ ] Déployer sur serveur de production
- [ ] Configurer monitoring des logs
