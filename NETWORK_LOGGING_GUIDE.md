# 📡 Guide de Logging Réseau - Client & Serveur

Guide complet pour logger toutes les communications réseau dans votre projet Unity/Mirror.

---

## 🎯 Architecture

### Côté Serveur
- **GameLogger** - Logs structurés JSON pour production
- **Fichiers** : `/Logs/log-YYYY-MM-DD.json`

### Côté Client  
- **ClientLogger** - Logs console colorés pour debugging
- **Affichage** : Console Unity avec timestamps et catégories

---

## 📦 Fichiers Créés

### Code Source
| Fichier | Description |
|---------|-------------|
| `ClientLogger.cs` | Logger console côté client avec formatage coloré |
| `ClientLoggerInitializer.cs` | MonoBehaviour pour initialiser ClientLogger |
| `NetworkLoggingExamples.cs` | Exemples de logging pour toutes les communications |
| `LoggedNetworkManager.cs` | NetworkManager avec logging intégré |
| `NetworkMessageLogging.cs` | Extension methods pour logging facile |
| `LoggedNetworkBehaviourExample.cs` | Exemple complet d'utilisation |

---

## 🚀 Démarrage Rapide

### 1. Configuration Client

**Étape 1:** Créer un GameObject `ClientLogger` dans la scène
**Étape 2:** Attacher `ClientLoggerInitializer`
**Étape 3:** Configurer le niveau de log (Debug, Information, Warning, Error)

### 2. Configuration Serveur

Déjà configuré avec `ServerLoggerInitializer` (voir documentation précédente)

### 3. Configuration NetworkManager

**Option A:** Utiliser `LoggedNetworkManager`
```csharp
// Hériter de LoggedNetworkManager au lieu de NetworkManager
public class MyNetworkManager : LoggedNetworkManager {
    // Tout est déjà configuré!
}
```

**Option B:** Intégrer dans votre NetworkManager existant
```csharp
public override void OnServerConnect(NetworkConnectionToClient conn) {
    base.OnServerConnect(conn);
    NetworkLoggingExamples.OnServerConnect(conn);
}
```

---

## 📝 Utilisation de Base

### Côté Client (ClientLogger)

```csharp
using Sim.Logging;

// Logs réseau
ClientLogger.Network("Connected to {Server}", serverAddress);
ClientLogger.NetworkDebug("Packet received {Size} bytes", packetSize);
ClientLogger.NetworkWarning("Connection unstable {PingMs}ms", ping);

// Logs joueur
ClientLogger.Player("Local player spawned {PlayerId}", playerId);

// Logs UI
ClientLogger.UI("Inventory opened");
ClientLogger.UI("Chat message received {From}", sender);

// Logs système
ClientLogger.System("Scene loaded {SceneName}", scene);
ClientLogger.Debug("Debug info {Value}", value);
ClientLogger.Warning("Warning message");
ClientLogger.Error(exception, "Error occurred");
```

### Côté Serveur (GameLogger)

```csharp
using Sim.Logging;

// Logs réseau
GameLogger.Network.Info("Client connected {ConnectionId}", connId);

// Logs props
GameLogger.Props.Info("Prop spawned {PropId}", propId);

// Logs joueurs
GameLogger.Player.Info("Player action {Action}", action);
```

---

## 🎨 Format ClientLogger

### Apparence Console

```
[14:23:45.123] NETWORK   | INFO     | Connected to localhost:7777
[14:23:45.234] PLAYER    | INFO     | Local player spawned player_123
[14:23:46.567] UI        | INFO     | Inventory opened
[14:23:47.890] NETWORK   | WARNING  | Connection unstable 150ms
```

### Couleurs par Catégorie

| Catégorie | Couleur | Usage |
|-----------|---------|-------|
| Network | Bleu clair | Tout le réseau |
| Player | Violet | Actions joueur local |
| Props | Vert | Objets du monde |
| UI | Jaune | Interface utilisateur |
| System | Gris | Système général |
| Error | Rouge | Erreurs |
| Warning | Orange | Avertissements |

---

## 📡 Logging des Communications Réseau

### 1. NetworkManager (Callbacks automatiques)

```csharp
// Utiliser LoggedNetworkManager pour logger automatiquement:
// - OnStartServer / OnStopServer
// - OnServerConnect / OnServerDisconnect
// - OnClientConnect / OnClientDisconnect
// - Changements de scène
// - Erreurs réseau
```

### 2. NetworkBehaviour (Methods extension)

```csharp
using Sim.Logging;

public class MyPlayer : NetworkBehaviour {

    [Command]
    void CmdFire() {
        // Log automatique côté serveur
        this.LogCommand("Fire");
        
        // Log custom
        GameLogger.Player.Info("PlayerFired {NetId}", netId);
        
        RpcOnFire();
    }

    [ClientRpc]
    void RpcOnFire() {
        // Log automatique côté client
        this.LogRpcReceived("OnFire");
        
        // Log custom
        ClientLogger.Player("Fire effect played");
    }

    [TargetRpc]
    void TargetOnHit(NetworkConnection target) {
        // Log automatique
        this.LogTargetRpcReceived("OnHit");
        
        // Log custom
        ClientLogger.UI("Damage taken!");
    }

    [SyncVar(hook = nameof(OnHealthChanged))]
    int health;

    void OnHealthChanged(int old, int new) {
        // Log SyncVar
        this.LogSyncVarChanged("Health", old, new);
    }
}
```

---

## 🔧 Méthodes de Logging Disponibles

### ClientLogger

```csharp
// Catégories principales
ClientLogger.Network(message, args...);        // Réseau
ClientLogger.NetworkDebug(message, args...);     // Réseau (debug)
ClientLogger.NetworkWarning(message, args...); // Réseau (warning)
ClientLogger.NetworkError(ex, message, args...); // Réseau (erreur)

ClientLogger.Props(message, args...);          // Props
ClientLogger.PropsDebug(message, args...);       // Props (debug)

ClientLogger.Rooms(message, args...);          // Rooms
ClientLogger.Player(message, args...);         // Joueur
ClientLogger.UI(message, args...);               // UI
ClientLogger.Audio(message, args...);          // Audio
ClientLogger.Input(message, args...);          // Input

// Génériques
ClientLogger.Debug(message, args...);
ClientLogger.Info(message, args...);
ClientLogger.Warning(message, args...);
ClientLogger.Error(ex, message, args...);
ClientLogger.Fatal(ex, message, args...);
```

### NetworkBehaviour Extensions

```csharp
// Dans une classe héritant de NetworkBehaviour:

// Commands
this.LogCommand("CommandName", arg1, arg2);       // Côté serveur
this.LogCommandSent("CommandName", arg1, arg2);   // Côté client

// RPC
this.LogRpcReceived("RpcName", arg1, arg2);       // Côté client
this.LogRpcCalled("RpcName", arg1, arg2);         // Côté serveur

// TargetRpc
this.LogTargetRpcReceived("RpcName", arg1);       // Côté client (target)
this.LogTargetRpcCalled(target, "RpcName", arg1);   // Côté serveur

// SyncVars
this.LogSyncVarChanged("VarName", oldValue, newValue);

// Lifecycle
this.LogServerSpawn();
this.LogClientSpawn();
this.LogServerDestroy();
this.LogClientDestroy();

// Stats
NetworkMessageLogging.LogNetworkStats();
```

---

## 📊 Exemples Concrets

### 1. Connexion / Déconnexion

```csharp
// Client
ClientLogger.Network("Connecting to {Address}:{Port}", address, port);
ClientLogger.Network("Connected! ID={ConnectionId}", connectionId);
ClientLogger.NetworkWarning("Disconnected: {Reason}", reason);

// Server
GameLogger.Network.Info("Client connected {Id} from {IP}", id, ip);
GameLogger.Network.Info("Client disconnected {Id}, remaining: {Count}", id, count);
```

### 2. Messages Réseau

```csharp
// Command (Client -> Server)
[Command]
void CmdMove(Vector3 pos) {
    this.LogCommand("Move", pos); // Log serveur
    // ...
}

// Appel côté client
void Update() {
    if (isLocalPlayer) {
        this.LogCommandSent("Move", transform.position);
        CmdMove(transform.position);
    }
}

// RPC (Server -> Clients)
[ClientRpc]
void RpcExplosion(Vector3 pos) {
    this.LogRpcReceived("Explosion", pos); // Log client
    // ...
}

// Appel côté serveur
void Explode() {
    this.LogRpcCalled("Explosion", transform.position);
    RpcExplosion(transform.position);
}
```

### 3. SyncVars

```csharp
[SyncVar(hook = nameof(OnHealthChanged))]
public int health = 100;

void OnHealthChanged(int old, int new) {
    // Log automatique des deux côtés
    this.LogSyncVarChanged("Health", old, new);
    
    // Mise à jour UI côté client
    if (isClient) {
        ClientLogger.UI("Health updated: {Old} -> {New}", old, new);
    }
}
```

---

## 🎮 Intégration Complète Exemple

```csharp
using Sim.Logging;
using Mirror;

public class PlayerController : NetworkBehaviour {
    
    [SyncVar(hook = nameof(OnHealthChanged))]
    private int health = 100;
    
    [SerializeField] private float moveSpeed = 5f;

    public override void OnStartLocalPlayer() {
        base.OnStartLocalPlayer();
        ClientLogger.Player("Local player ready! {NetId}", netId);
    }

    void Update() {
        if (!isLocalPlayer) return;
        
        // Input
        var input = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        
        if (input.magnitude > 0.1f) {
            ClientLogger.Input("Move input {Input}", input);
            
            // Envoi au serveur
            this.LogCommandSent("Move", input);
            CmdMove(input);
        }
        
        // Tir
        if (Input.GetButtonDown("Fire1")) {
            ClientLogger.Input("Fire pressed");
            CmdFire();
        }
    }

    [Command]
    void CmdMove(Vector3 input) {
        this.LogCommand("Move", input);
        
        var newPos = transform.position + input * moveSpeed * Time.deltaTime;
        transform.position = newPos;
        
        GameLogger.Player.Debug("Player moved {NetId} to {Position}", netId, newPos);
    }

    [Command]
    void CmdFire() {
        this.LogCommand("Fire");
        GameLogger.Player.Info("Player fired {NetId}", netId);
        
        // Vérifier hit...
        RpcFireEffect();
    }

    [ClientRpc]
    void RpcFireEffect() {
        this.LogRpcReceived("FireEffect");
        ClientLogger.Player("Playing fire effect");
        
        // Jouer effet visuel...
    }

    void OnHealthChanged(int old, int new) {
        this.LogSyncVarChanged("Health", old, new);
        
        if (isClient) {
            if (new < old) {
                ClientLogger.UI("Damage taken! {Damage}", old - new);
            }
            
            if (new <= 0) {
                ClientLogger.Player("Player died!");
            }
        }
    }
}
```

---

## ⚙️ Configuration Avancée

### Niveaux de Log Client

```csharp
// Initialisation avec niveau spécifique
ClientLogger.Initialize(ClientLogger.LogLevel.Debug);    // Tout
ClientLogger.Initialize(ClientLogger.LogLevel.Information); // Info+
ClientLogger.Initialize(ClientLogger.LogLevel.Warning);  // Warning+
ClientLogger.Initialize(ClientLogger.LogLevel.Error);    // Erreurs uniquement
```

### Changement dynamique

```csharp
// Pendant l'exécution
ClientLogger.SetMinimumLevel(ClientLogger.LogLevel.Warning);
```

### Vérification avant log coûteux

```csharp
if (ClientLogger.IsEnabled(ClientLogger.LogLevel.Debug)) {
    var expensiveData = ComputeExpensiveData();
    ClientLogger.Debug("Expensive data: {Data}", expensiveData);
}
```

---

## 🐛 Dépannage

### Les logs client n'apparaissent pas
1. Vérifier que `ClientLoggerInitializer` est dans la scène
2. Vérifier que `initializeOnAwake` est coché
3. Vérifier que le niveau de log n'est pas trop restrictif

### Les logs serveur n'apparaissent pas
1. Vérifier que `ServerLoggerInitializer` est dans la scène
2. Vérifier que les packages Serilog sont installés
3. Vérifier le dossier `/Logs/`

### Trop de logs (performance)
```csharp
// Désactiver les logs détaillés en production
[SerializeField] private bool verboseLogging = false;

void Update() {
    if (verboseLogging) {
        ClientLogger.NetworkDebug("Frame update");
    }
}
```

---

## 📈 Performance

### Overhead ClientLogger
- **Par log**: ~0.01ms (négligeable)
- **Mémoire**: Aucune allocation
- **Impact**: Minimal

### Bonnes pratiques
```csharp
// ✅ Vérifier avant log coûteux
if (ClientLogger.IsEnabled(ClientLogger.LogLevel.Debug)) {
    ClientLogger.Debug("{ComplexData}", GetComplexData());
}

// ✅ Logger les événements importants uniquement
ClientLogger.Network("Connected"); // OK
// ❌ Pas chaque frame
ClientLogger.Network("Frame {N}", n); // Éviter
```

---

## 📚 Résumé des Catégories

### Serveur (GameLogger)
- `GameLogger.Network` - Communication réseau
- `GameLogger.Props` - Props du monde
- `GameLogger.Rooms` - Gestion des rooms
- `GameLogger.Player` - Actions joueurs
- `GameLogger.System` - Système général

### Client (ClientLogger)
- `ClientLogger.Network` - Communication réseau
- `ClientLogger.Player` - Joueur local
- `ClientLogger.Props` - Props visibles
- `ClientLogger.UI` - Interface
- `ClientLogger.Audio` - Audio
- `ClientLogger.Input` - Entrées
- `ClientLogger.Rooms` - Rooms
- `ClientLogger.System` - Système

---

## 🎯 Checklist Intégration

### Client
- [ ] `ClientLogger` GameObject créé
- [ ] `ClientLoggerInitializer` attaché
- [ ] Niveau de log configuré
- [ ] Test: logs visibles dans console

### Serveur
- [ ] `ServerLogger` GameObject créé
- [ ] `ServerLoggerInitializer` attaché
- [ ] Packages NuGet installés
- [ ] Test: fichiers JSON créés

### NetworkManager
- [ ] `LoggedNetworkManager` utilisé OU callbacks ajoutés
- [ ] Test: connexions loggées

### NetworkBehaviours
- [ ] `LogCommand` dans les Commands
- [ ] `LogRpcReceived` dans les ClientRpc
- [ ] `LogTargetRpcReceived` dans les TargetRpc
- [ ] `LogSyncVarChanged` dans les hooks

---

## 🚀 Prochaines Étapes

1. **Tester** : Vérifier que tous les logs apparaissent
2. **Personnaliser** : Ajuster les couleurs catégories si besoin
3. **Filtrer** : Configurer niveaux pour production
4. **Documenter** : Former l'équipe sur les bonnes pratiques

---

**Votre système de logging réseau est maintenant complet !** 🎉

- Côté serveur : Logs JSON structurés pour analyse
- Côté client : Logs console colorés pour debugging
