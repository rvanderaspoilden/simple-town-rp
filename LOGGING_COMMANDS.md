# 🛠️ Commandes Utiles - Logging System

Référence rapide des commandes et configurations pour le système de logging.

---

## 🪟 Variables d'Environnement Windows

### Configuration de base

```batch
# Définir le niveau de log
SET LOG_LEVEL=Information

# Définir l'environnement
SET ENVIRONMENT=Production

# Activer Seq
SET SEQ_ENABLED=true
SET SEQ_URL=http://localhost:5341

# Avec API Key Seq (production)
SET SEQ_API_KEY=your-api-key-here
```

### Niveaux disponibles

```batch
SET LOG_LEVEL=Debug        # Très verbeux (dev uniquement)
SET LOG_LEVEL=Information  # Par défaut (production)
SET LOG_LEVEL=Warning      # Moins verbeux
SET LOG_LEVEL=Error        # Erreurs uniquement
SET LOG_LEVEL=Fatal        # Critique uniquement
```

### Rendre permanent (Windows)

```batch
# Via ligne de commande (admin)
setx LOG_LEVEL "Information" /M
setx ENVIRONMENT "Production" /M
setx SEQ_ENABLED "true" /M
setx SEQ_URL "http://localhost:5341" /M

# Ou via GUI
# Panneau de configuration > Système > Paramètres système avancés
# > Variables d'environnement > Nouvelles variables système
```

---

## 🐧 Variables d'Environnement Linux

### Configuration de base

```bash
# Définir les variables
export LOG_LEVEL=Information
export ENVIRONMENT=Production
export SEQ_ENABLED=true
export SEQ_URL=http://localhost:5341
export SEQ_API_KEY=your-api-key-here
```

### Rendre permanent

```bash
# Ajouter au ~/.bashrc ou ~/.profile
echo 'export LOG_LEVEL=Information' >> ~/.bashrc
echo 'export ENVIRONMENT=Production' >> ~/.bashrc
echo 'export SEQ_ENABLED=true' >> ~/.bashrc
echo 'export SEQ_URL=http://localhost:5341' >> ~/.bashrc

# Recharger
source ~/.bashrc
```

### Pour serveur headless Unity

```bash
# Lancer Unity avec variables
LOG_LEVEL=Information \
ENVIRONMENT=Production \
SEQ_ENABLED=true \
SEQ_URL=http://localhost:5341 \
./SimpleTownRP.x86_64 -batchmode -nographics
```

---

## 🔍 Seq - Commandes

### Installation

```bash
# Windows - Télécharger et installer
# https://datalust.co/download

# Linux - Docker
docker run --name seq -d --restart unless-stopped \
  -e ACCEPT_EULA=Y \
  -p 5341:80 \
  -v /path/to/seq/data:/data \
  datalust/seq:latest

# Linux - Package
wget https://datalust.co/seq-latest.deb
sudo dpkg -i seq-latest.deb
```

### Gestion du service

```bash
# Windows
net start Seq
net stop Seq
net restart Seq

# Linux (systemd)
sudo systemctl start seq
sudo systemctl stop seq
sudo systemctl restart seq
sudo systemctl status seq
```

### Accès

```bash
# Local
http://localhost:5341

# Distant
http://your-server-ip:5341
```

---

## 🔥 Firewall Windows

### Ouvrir le port Seq (5341)

```batch
# Autoriser Seq
netsh advfirewall firewall add rule ^
  name="Seq Log Server" ^
  dir=in ^
  action=allow ^
  protocol=TCP ^
  localport=5341

# Vérifier les règles
netsh advfirewall firewall show rule name="Seq Log Server"

# Supprimer la règle
netsh advfirewall firewall delete rule name="Seq Log Server"
```

---

## 📊 Seq API - Requêtes

### Récupérer les logs via API

```bash
# Tous les logs
curl http://localhost:5341/api/events

# Avec filtre
curl "http://localhost:5341/api/events?filter=Category%3D%22Props%22"

# Avec API Key
curl http://localhost:5341/api/events \
  -H "X-Seq-ApiKey: your-api-key"

# Export JSON
curl "http://localhost:5341/api/events?filter=@Level%3D%22Error%22" \
  -H "X-Seq-ApiKey: your-api-key" \
  -o errors.json
```

### Créer un événement via API

```bash
curl -X POST http://localhost:5341/api/events/raw \
  -H "Content-Type: application/vnd.serilog.clef" \
  -H "X-Seq-ApiKey: your-api-key" \
  -d '{"@t":"2026-05-07T10:00:00Z","@mt":"Test event","@l":"Information"}'
```

---

## 🗂️ Gestion des Fichiers de Logs

### Localisation

```bash
# Windows
D:\Workspace\Unity\TheBroz\simple-town-rp\Logs\

# Linux
/path/to/game/Logs/
```

### Lister les logs

```bash
# Windows
dir Logs\*.json

# Linux
ls -lh Logs/*.json
```

### Voir les derniers logs

```bash
# Windows (PowerShell)
Get-Content Logs\log-2026-05-07.json -Tail 50

# Linux
tail -n 50 Logs/log-2026-05-07.json
```

### Rechercher dans les logs

```bash
# Windows (PowerShell)
Select-String -Path "Logs\*.json" -Pattern "PropSpawned"

# Linux
grep "PropSpawned" Logs/*.json
```

### Compter les erreurs

```bash
# Windows (PowerShell)
(Select-String -Path "Logs\*.json" -Pattern '"@l":"Error"').Count

# Linux
grep -c '"@l":"Error"' Logs/*.json
```

### Nettoyer les vieux logs

```bash
# Windows (PowerShell) - Supprimer logs > 30 jours
Get-ChildItem Logs\*.json | Where-Object {$_.LastWriteTime -lt (Get-Date).AddDays(-30)} | Remove-Item

# Linux - Supprimer logs > 30 jours
find Logs/ -name "*.json" -mtime +30 -delete
```

### Archiver les logs

```bash
# Windows (PowerShell)
Compress-Archive -Path Logs\log-2026-04-*.json -DestinationPath Archives\logs-2026-04.zip

# Linux
tar -czf Archives/logs-2026-04.tar.gz Logs/log-2026-04-*.json
```

---

## 🧪 Tests & Debugging

### Test rapide du logger

```csharp
// Dans Unity Console ou script de test
using Sim.Logging;

GameLogger.System.Info("Test log {Timestamp}", System.DateTime.Now);
```

### Vérifier que Serilog fonctionne

```csharp
using Serilog;

try {
    Log.Information("Serilog test");
    Debug.Log("✅ Serilog OK");
} catch (System.Exception ex) {
    Debug.LogError($"❌ Serilog error: {ex.Message}");
}
```

### Forcer un flush des logs

```csharp
using Serilog;

Log.CloseAndFlush();
```

---

## 📈 Monitoring & Métriques

### Compter les logs par niveau (PowerShell)

```powershell
$logs = Get-Content Logs\log-2026-05-07.json | ConvertFrom-Json
$logs | Group-Object '@l' | Select-Object Name, Count
```

### Extraire les erreurs (PowerShell)

```powershell
$logs = Get-Content Logs\log-2026-05-07.json | ConvertFrom-Json
$errors = $logs | Where-Object { $_.'@l' -eq 'Error' }
$errors | Format-Table '@t', '@mt', 'PropId', 'RoomId'
```

### Statistiques par catégorie (PowerShell)

```powershell
$logs = Get-Content Logs\log-2026-05-07.json | ConvertFrom-Json
$logs | Group-Object 'Category' | Select-Object Name, Count | Sort-Object Count -Descending
```

---

## 🔄 Migration - Recherche & Remplacement

### Trouver tous les Debug.Log (Regex)

```regex
Debug\.Log\([^)]+\)
Debug\.LogWarning\([^)]+\)
Debug\.LogError\([^)]+\)
Debug\.LogException\([^)]+\)
```

### Trouver string interpolation (Regex)

```regex
\$"[^"]*\{[^}]+\}[^"]*"
```

### Recherche dans tous les fichiers C# (PowerShell)

```powershell
# Trouver tous les Debug.Log
Get-ChildItem -Path Assets\Scripts -Recurse -Filter *.cs | 
  Select-String -Pattern "Debug\.Log" | 
  Select-Object Path, LineNumber, Line
```

---

## 🚀 Déploiement Production

### Build Unity avec logging

```bash
# Windows
Unity.exe -quit -batchmode -projectPath "D:\Workspace\Unity\TheBroz\simple-town-rp" -buildWindows64Player "Build\Server.exe"

# Linux
Unity -quit -batchmode -projectPath "/path/to/project" -buildLinux64Player "Build/Server.x86_64"
```

### Lancer le serveur avec configuration

```bash
# Windows
SET LOG_LEVEL=Information
SET ENVIRONMENT=Production
SET SEQ_ENABLED=true
SET SEQ_URL=http://localhost:5341
Server.exe

# Linux
LOG_LEVEL=Information \
ENVIRONMENT=Production \
SEQ_ENABLED=true \
SEQ_URL=http://localhost:5341 \
./Server.x86_64 -batchmode -nographics
```

---

## 📦 Backup & Restauration

### Backup des logs

```bash
# Windows (PowerShell)
$date = Get-Date -Format "yyyy-MM-dd"
Compress-Archive -Path Logs\* -DestinationPath "Backups\logs-$date.zip"

# Linux
tar -czf Backups/logs-$(date +%Y-%m-%d).tar.gz Logs/
```

### Restauration

```bash
# Windows (PowerShell)
Expand-Archive -Path Backups\logs-2026-05-07.zip -DestinationPath Logs\

# Linux
tar -xzf Backups/logs-2026-05-07.tar.gz -C Logs/
```

---

## 🔐 Sécurité

### Créer une API Key Seq

```bash
# Via interface web
# Settings > API Keys > New API Key

# Via API
curl -X POST http://localhost:5341/api/apikeys \
  -H "Content-Type: application/json" \
  -H "X-Seq-ApiKey: admin-key" \
  -d '{"Title":"Unity Server","Permissions":["Ingest"]}'
```

### Vérifier les permissions des logs

```bash
# Windows
icacls Logs

# Linux
ls -la Logs/
chmod 750 Logs/
```

---

## 📚 Ressources

- **Serilog Docs**: https://serilog.net/
- **Seq Docs**: https://docs.datalust.co/
- **Seq API**: https://docs.datalust.co/reference
- **Unity Command Line**: https://docs.unity3d.com/Manual/CommandLineArguments.html

---

## 💡 Astuces

### Alias PowerShell utiles

```powershell
# Ajouter à $PROFILE

function Show-Logs {
    Get-Content Logs\log-$(Get-Date -Format "yyyy-MM-dd").json -Tail 50 -Wait
}

function Count-Errors {
    (Select-String -Path "Logs\*.json" -Pattern '"@l":"Error"').Count
}

function Open-Seq {
    Start-Process "http://localhost:5341"
}
```

### Alias Bash utiles

```bash
# Ajouter à ~/.bashrc

alias logs-tail='tail -f Logs/log-$(date +%Y-%m-%d).json'
alias logs-errors='grep "@l\":\"Error\"" Logs/*.json'
alias seq-open='xdg-open http://localhost:5341'
```

---

**🎯 Référence rapide pour gérer le système de logging au quotidien!**
