# 🚀 Quick Start - Logging Structuré

Guide de démarrage rapide pour mettre en place le système de logging en 15 minutes.

---

## ⏱️ Installation Rapide (5 minutes)

### Étape 1: Installer les packages NuGet

**Via NuGet for Unity:**
1. Ouvrir Unity
2. Menu: `NuGet > Manage NuGet Packages`
3. Rechercher et installer:
   - `Serilog` (3.1.1+)
   - `Serilog.Sinks.File` (5.0.0+)
   - `Serilog.Formatting.Compact` (2.0.0+)
   - `Serilog.Enrichers.Thread` (3.1.0+)
   - `Serilog.Sinks.Seq` (6.0.0+) [optionnel]

**Ou manuellement:**
- Télécharger les DLLs depuis nuget.org
- Copier dans `Assets/Plugins/Serilog/`

### Étape 2: Ajouter le logger à la scène

1. Créer un GameObject vide: `ServerLogger`
2. Attacher le script: `ServerLoggerInitializer`
3. Configurer:
   - ✅ Initialize On Awake
   - ✅ Server Only

### Étape 3: Tester

```csharp
using Sim.Logging;

void Start() {
    GameLogger.System.Info("ServerStarted {Version}", Application.version);
}
```

Vérifier que `/Logs/log-YYYY-MM-DD.json` est créé.

---

## 📝 Utilisation de Base (5 minutes)

### Remplacer Debug.Log

**Avant:**
```csharp
Debug.Log($"Player {playerId} entered room {roomId}");
```

**Après:**
```csharp
GameLogger.Rooms.Info("PlayerEnteredRoom {PlayerId} {RoomId}", playerId, roomId);
```

### Catégories disponibles

```csharp
GameLogger.Network.Info(...)   // Logs réseau
GameLogger.Props.Info(...)     // Logs props
GameLogger.Rooms.Info(...)     // Logs rooms
GameLogger.Player.Info(...)    // Logs joueurs
GameLogger.System.Info(...)    // Logs système
GameLogger.Info(...)           // Logs génériques
```

### Niveaux de log

```csharp
GameLogger.*.Debug(...)    // Développement uniquement
GameLogger.*.Info(...)     // Événements importants
GameLogger.*.Warning(...)  // Situations anormales
GameLogger.*.Error(ex, ...)  // Erreurs
GameLogger.Fatal(ex, ...)  // Erreurs critiques
```

---

## 🎯 Exemples Concrets (5 minutes)

### Exemple 1: Spawn de prop

```csharp
public int SpawnProp(string roomId, int prefabId, Vector3 position) {
    try {
        var propId = _nextId++;
        var go = Instantiate(prefab, position, Quaternion.identity);
        
        GameLogger.Props.Info("PropSpawned {PropId} {RoomId} {PrefabId} Position={Position}", 
            propId, roomId, prefabId, position);
        
        return propId;
    }
    catch (Exception ex) {
        GameLogger.Props.Error(ex, "PropSpawnFailed {PrefabId} {RoomId}", prefabId, roomId);
        return -1;
    }
}
```

### Exemple 2: Connexion réseau

```csharp
void OnServerConnect(NetworkConnectionToClient conn) {
    GameLogger.Network.Info("ClientConnected {ConnectionId} {Address}", 
        conn.connectionId, conn.address);
}
```

### Exemple 3: Entrée dans une room

```csharp
public void OnPlayerEnterRoom(string playerId, string roomId) {
    GameLogger.Rooms.Info("PlayerEnteredRoom {PlayerId} {RoomId}", playerId, roomId);
}
```

### Exemple 4: Performance monitoring

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
ProcessHeavyOperation();
sw.Stop();

if (sw.ElapsedMilliseconds > 100) {
    GameLogger.System.Warning("SlowOperation {Operation} {DurationMs}", 
        "ProcessHeavyOperation", sw.ElapsedMilliseconds);
}
```

---

## ⚙️ Configuration (Optionnel)

### Variables d'environnement

```batch
# Windows
SET LOG_LEVEL=Information
SET ENVIRONMENT=Production
SET SEQ_ENABLED=true
SET SEQ_URL=http://localhost:5341
```

### Niveaux de log

- `Debug` - Très verbeux (dev uniquement)
- `Information` - Par défaut (production)
- `Warning` - Moins verbeux
- `Error` - Erreurs uniquement

---

## 🔍 Visualisation avec Seq (Optionnel)

### Installation Seq

1. Télécharger: https://datalust.co/download
2. Installer (service Windows)
3. Ouvrir: http://localhost:5341

### Activer Seq

```batch
SET SEQ_ENABLED=true
SET SEQ_URL=http://localhost:5341
```

### Requêtes utiles

```sql
-- Tous les spawns de props
Category = "Props" AND @MessageTemplate LIKE "%PropSpawned%"

-- Erreurs uniquement
@Level IN ["Error", "Fatal"]

-- Activité d'un joueur
PlayerId = "player_123"

-- Room spécifique
RoomId = "room_apartment_123"
```

---

## 📊 Format des Logs JSON

Exemple de log généré:

```json
{
  "@t": "2026-05-07T10:29:15.1234567Z",
  "@mt": "PropSpawned {PropId} {RoomId} {PrefabId}",
  "@l": "Information",
  "PropId": 42,
  "RoomId": "room_abc123",
  "PrefabId": 101,
  "Category": "Props",
  "Application": "SimpleTownRP",
  "Environment": "Production",
  "MachineName": "SERVER-01"
}
```

---

## ✅ Checklist

### Installation
- [ ] Packages NuGet installés
- [ ] `ServerLoggerInitializer` ajouté à la scène
- [ ] Test: logs créés dans `/Logs/`

### Migration du code
- [ ] `using Sim.Logging;` ajouté
- [ ] `Debug.Log` remplacé par `GameLogger.*`
- [ ] String interpolation `$""` remplacée par templates `"... {Param}"`
- [ ] Contexte ajouté (PlayerId, RoomId, PropId)

### Configuration (Optionnel)
- [ ] Variables d'environnement configurées
- [ ] Seq installé et configuré
- [ ] Logs visibles dans Seq

---

## 🚨 Règles Importantes

### ✅ À FAIRE

```csharp
// Message templates avec paramètres nommés
GameLogger.Props.Info("PropSpawned {PropId} {RoomId}", propId, roomId);

// Inclure le contexte (IDs)
GameLogger.Rooms.Info("PlayerEnteredRoom {PlayerId} {RoomId}", playerId, roomId);

// Logger les exceptions avec contexte
GameLogger.Props.Error(ex, "PropSpawnFailed {PropId}", propId);

// Vérifier le niveau pour logs coûteux
if (GameLogger.IsEnabled(LogEventLevel.Debug)) {
    GameLogger.Debug("ExpensiveDebugInfo {Data}", ComputeExpensiveData());
}
```

### ❌ À NE PAS FAIRE

```csharp
// ❌ String interpolation
GameLogger.Info($"Player {playerId} entered");

// ❌ Concaténation
GameLogger.Info("Prop: " + propId);

// ❌ Logs sans contexte
GameLogger.Info("Something happened");

// ❌ Informations sensibles
GameLogger.Info("Password {Password}", password);
```

---

## 📚 Documentation Complète

- **`LOGGING_SETUP.md`** - Configuration détaillée
- **`MIGRATION_GUIDE.md`** - Guide de migration du code
- **`NUGET_PACKAGES.md`** - Installation des packages
- **`SEQ_SETUP.md`** - Configuration de Seq
- **`INTEGRATION_EXAMPLES.cs`** - Exemples de code

---

## 🆘 Dépannage Rapide

### Les logs ne sont pas créés

1. Vérifier que `LoggerBootstrap.Initialize()` est appelé
2. Vérifier les permissions du dossier `/Logs/`
3. Consulter la console Unity pour erreurs

### Logs trop verbeux

```batch
SET LOG_LEVEL=Warning
```

### Seq ne reçoit pas les logs

1. Vérifier que Seq est démarré: http://localhost:5341
2. Vérifier `SEQ_ENABLED=true`
3. Vérifier le firewall

---

## 💡 Conseils

1. **Commencez petit** - Migrez un fichier à la fois
2. **Testez en local** - Avant de déployer en production
3. **Utilisez Seq** - Pour visualiser et débugger
4. **Ajoutez du contexte** - PlayerId, RoomId, PropId dans chaque log
5. **Surveillez les performances** - Utilisez `IsEnabled()` pour logs Debug

---

## 🎯 Prochaines Étapes

1. ✅ Installation et test de base (ce guide)
2. 📝 Migrer `ServerPropManager` (voir `MIGRATION_GUIDE.md`)
3. 🌐 Migrer les handlers réseau
4. 🏠 Migrer la gestion des rooms
5. 👤 Migrer les actions joueurs
6. 📊 Configurer Seq et créer des dashboards
7. 🚀 Déployer en production

---

## 📞 Support

- **Documentation Serilog**: https://serilog.net/
- **Documentation Seq**: https://docs.datalust.co/
- **Exemples de code**: `Assets/Scripts/Utils/Logging/`

---

**Temps total estimé: 15 minutes** ⏱️

Bonne chance! 🚀
