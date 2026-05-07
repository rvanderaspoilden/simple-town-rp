# 🔍 Configuration de Seq pour la Visualisation des Logs

Seq est un outil de visualisation et d'analyse de logs structurés, parfait pour Serilog.

---

## 📥 Installation de Seq

### Windows (Recommandé pour votre serveur)

1. **Télécharger Seq**
   - URL: https://datalust.co/download
   - Télécharger la version Windows Installer (.msi)

2. **Installer Seq**
   ```
   - Double-cliquer sur le fichier .msi
   - Suivre l'assistant d'installation
   - Seq s'installera comme service Windows
   - Port par défaut: 5341
   ```

3. **Vérifier l'installation**
   - Ouvrir un navigateur
   - Aller à: http://localhost:5341
   - Vous devriez voir l'interface Seq

### Linux (Pour serveur headless)

```bash
# Installer via Docker
docker run --name seq -d --restart unless-stopped \
  -e ACCEPT_EULA=Y \
  -p 5341:80 \
  -v /path/to/seq/data:/data \
  datalust/seq:latest

# Ou via package
wget https://datalust.co/seq-latest.deb
sudo dpkg -i seq-latest.deb
```

---

## ⚙️ Configuration du Serveur Unity

### 1. Activer Seq dans le code

Deux options:

#### Option A: Variables d'environnement (Recommandé)

```batch
# Windows
SET SEQ_ENABLED=true
SET SEQ_URL=http://localhost:5341

# Linux
export SEQ_ENABLED=true
export SEQ_URL=http://localhost:5341
```

#### Option B: Modifier LoggerBootstrap.cs

```csharp
// Dans LoggerBootstrap.cs, ligne ~40
configuration.WriteTo.Seq("http://localhost:5341");
```

### 2. Redémarrer le serveur Unity

Les logs commenceront à apparaître dans Seq automatiquement.

---

## 🎯 Utilisation de Seq

### Interface Principale

1. **Stream** (vue en temps réel)
   - Affiche les logs au fur et à mesure
   - Auto-refresh toutes les 2 secondes

2. **Events** (recherche)
   - Recherche dans l'historique
   - Filtres avancés

3. **Dashboards** (métriques)
   - Créer des graphiques personnalisés
   - Monitorer des KPIs

---

## 🔍 Requêtes Seq Utiles

### Syntaxe de base

Seq utilise un langage de requête SQL-like.

### Exemples de Requêtes

#### 1. Tous les spawns de props
```sql
Category = "Props" AND @MessageTemplate LIKE "%PropSpawned%"
```

#### 2. Erreurs dans une room spécifique
```sql
RoomId = "room_apartment_123" AND @Level = "Error"
```

#### 3. Props spawned dans les 5 dernières minutes
```sql
Category = "Props" 
AND @MessageTemplate LIKE "%PropSpawned%" 
AND @Timestamp > Now() - 5m
```

#### 4. Activité d'un joueur spécifique
```sql
PlayerId = "player_abc123"
```

#### 5. Erreurs réseau
```sql
Category = "Network" AND @Level IN ["Error", "Fatal"]
```

#### 6. Opérations lentes (> 100ms)
```sql
@MessageTemplate LIKE "%SlowOperation%" AND DurationMs > 100
```

#### 7. Tous les logs d'une room
```sql
RoomId = "room_apartment_123"
```

#### 8. Props par type
```sql
Category = "Props" 
AND @MessageTemplate LIKE "%PropSpawned%" 
AND Type = "Furniture"
```

#### 9. Connexions/Déconnexions
```sql
Category = "Network" 
AND (@MessageTemplate LIKE "%ClientConnected%" OR @MessageTemplate LIKE "%ClientDisconnected%")
```

#### 10. Warnings et erreurs uniquement
```sql
@Level IN ["Warning", "Error", "Fatal"]
```

---

## 📊 Créer des Dashboards

### Dashboard: Vue d'ensemble du serveur

1. **Créer un nouveau dashboard**
   - Cliquer sur "Dashboards" > "New Dashboard"
   - Nom: "Server Overview"

2. **Ajouter des charts**

#### Chart 1: Logs par niveau
```sql
SELECT @Level, COUNT(*) 
FROM stream 
WHERE @Timestamp > Now() - 1h 
GROUP BY @Level
```

#### Chart 2: Props spawned par heure
```sql
SELECT COUNT(*) 
FROM stream 
WHERE Category = "Props" 
  AND @MessageTemplate LIKE "%PropSpawned%" 
  AND @Timestamp > Now() - 24h 
GROUP BY TIME(1h)
```

#### Chart 3: Erreurs par catégorie
```sql
SELECT Category, COUNT(*) 
FROM stream 
WHERE @Level IN ["Error", "Fatal"] 
  AND @Timestamp > Now() - 1h 
GROUP BY Category
```

#### Chart 4: Joueurs actifs (entrées de room)
```sql
SELECT COUNT(DISTINCT PlayerId) 
FROM stream 
WHERE @MessageTemplate LIKE "%PlayerEnteredRoom%" 
  AND @Timestamp > Now() - 1h
```

---

## 🚨 Alertes

### Créer une alerte pour les erreurs critiques

1. **Aller dans Settings > Alerts**
2. **Créer une nouvelle alerte**
   - Nom: "Critical Errors"
   - Requête:
     ```sql
     @Level = "Fatal" OR (@Level = "Error" AND Category = "System")
     ```
   - Condition: "When the query returns results"
   - Action: Email / Webhook / Slack

### Exemple d'alerte: Performance dégradée

```sql
@MessageTemplate LIKE "%SlowOperation%" 
AND DurationMs > 500 
AND @Timestamp > Now() - 5m
```

---

## 📈 Métriques Recommandées

### 1. Taux de spawn de props
```sql
SELECT COUNT(*) 
FROM stream 
WHERE @MessageTemplate LIKE "%PropSpawned%" 
  AND @Timestamp > Now() - 1h 
GROUP BY TIME(5m)
```

### 2. Latence moyenne des opérations
```sql
SELECT AVG(DurationMs) 
FROM stream 
WHERE DurationMs IS NOT NULL 
  AND @Timestamp > Now() - 1h 
GROUP BY TIME(5m)
```

### 3. Taux d'erreur
```sql
SELECT 
  (COUNT(*) FILTER (WHERE @Level IN ["Error", "Fatal"]) * 100.0 / COUNT(*)) AS ErrorRate
FROM stream 
WHERE @Timestamp > Now() - 1h 
GROUP BY TIME(5m)
```

### 4. Connexions actives
```sql
SELECT 
  SUM(CASE WHEN @MessageTemplate LIKE "%ClientConnected%" THEN 1 ELSE -1 END) AS ActiveConnections
FROM stream 
WHERE (@MessageTemplate LIKE "%ClientConnected%" OR @MessageTemplate LIKE "%ClientDisconnected%")
  AND @Timestamp > Now() - 1h
```

---

## 🎨 Personnalisation

### Filtres sauvegardés

Créer des filtres pour un accès rapide:

1. **Props uniquement**
   ```sql
   Category = "Props"
   ```

2. **Erreurs et warnings**
   ```sql
   @Level IN ["Warning", "Error", "Fatal"]
   ```

3. **Room spécifique**
   ```sql
   RoomId = "room_apartment_123"
   ```

### Colonnes personnalisées

Afficher les colonnes pertinentes:
- `@Timestamp`
- `@Level`
- `Category`
- `@Message`
- `PlayerId`
- `RoomId`
- `PropId`

---

## 🔐 Sécurité

### Authentification (Production)

1. **Activer l'authentification**
   - Settings > Users & Authentication
   - Créer un compte admin

2. **Créer des API Keys**
   - Pour le serveur Unity
   - Settings > API Keys > New API Key

3. **Utiliser l'API Key dans Unity**
   ```batch
   SET SEQ_URL=http://localhost:5341
   SET SEQ_API_KEY=your-api-key-here
   ```

   ```csharp
   // Dans LoggerBootstrap.cs
   configuration.WriteTo.Seq(
       serverUrl: seqUrl,
       apiKey: Environment.GetEnvironmentVariable("SEQ_API_KEY")
   );
   ```

---

## 🌐 Accès Distant

### Configuration pour accès externe

1. **Ouvrir le port dans le firewall**
   ```batch
   netsh advfirewall firewall add rule name="Seq" dir=in action=allow protocol=TCP localport=5341
   ```

2. **Configurer Seq pour écouter sur toutes les interfaces**
   - Éditer: `C:\ProgramData\Seq\Seq.json`
   - Changer `ListenUris` à: `["http://*:5341"]`
   - Redémarrer le service Seq

3. **Accéder depuis un autre PC**
   ```
   http://your-server-ip:5341
   ```

---

## 📊 Export de Données

### Export CSV
```sql
-- Requête
Category = "Props" AND @Timestamp > Now() - 24h

-- Cliquer sur "Export" > "CSV"
```

### Export JSON
```sql
-- Via API
curl http://localhost:5341/api/events?filter=Category%3D%22Props%22 \
  -H "X-Seq-ApiKey: your-api-key"
```

---

## 🔧 Maintenance

### Rétention des logs

Par défaut, Seq conserve les logs indéfiniment.

1. **Configurer la rétention**
   - Settings > Retention
   - Exemple: Conserver 30 jours

2. **Archivage**
   - Exporter les vieux logs en JSON
   - Stocker dans un système de backup

### Nettoyage

```sql
-- Supprimer les logs de debug > 7 jours
@Level = "Debug" AND @Timestamp < Now() - 7d
```

---

## 💡 Astuces

1. **Utiliser les signaux**
   - Créer des "Signals" pour des patterns récurrents
   - Exemple: Signal "High Error Rate" si > 10 erreurs/min

2. **Favoris**
   - Sauvegarder vos requêtes fréquentes
   - Accès rapide via le menu

3. **Raccourcis clavier**
   - `Ctrl+K`: Recherche rapide
   - `Ctrl+Enter`: Exécuter la requête
   - `Esc`: Effacer les filtres

4. **Live tail**
   - Mode "Stream" pour voir les logs en temps réel
   - Utile pour le debugging actif

---

## 📚 Ressources

- **Documentation Seq**: https://docs.datalust.co/
- **Requêtes Seq**: https://docs.datalust.co/docs/the-seq-query-language
- **API Seq**: https://docs.datalust.co/reference
- **Serilog + Seq**: https://github.com/serilog/serilog-sinks-seq

---

## ✅ Checklist de Configuration

- [ ] Seq installé et accessible sur http://localhost:5341
- [ ] Variables d'environnement configurées (SEQ_ENABLED, SEQ_URL)
- [ ] Serveur Unity redémarré
- [ ] Logs visibles dans Seq
- [ ] Dashboard "Server Overview" créé
- [ ] Alertes configurées pour erreurs critiques
- [ ] Authentification activée (production)
- [ ] Rétention configurée (30 jours)
- [ ] Firewall configuré (si accès distant)
- [ ] Documentation partagée avec l'équipe
