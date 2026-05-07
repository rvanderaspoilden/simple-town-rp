# 📦 Packages NuGet Requis pour le Logging

## Packages à Installer

### 1. Serilog (Core)
- **Package**: `Serilog`
- **Version**: 3.1.1 ou supérieur
- **URL**: https://www.nuget.org/packages/Serilog

### 2. Serilog.Sinks.File
- **Package**: `Serilog.Sinks.File`
- **Version**: 5.0.0 ou supérieur
- **URL**: https://www.nuget.org/packages/Serilog.Sinks.File

### 3. Serilog.Formatting.Compact
- **Package**: `Serilog.Formatting.Compact`
- **Version**: 2.0.0 ou supérieur
- **URL**: https://www.nuget.org/packages/Serilog.Formatting.Compact

### 4. Serilog.Enrichers.Thread
- **Package**: `Serilog.Enrichers.Thread`
- **Version**: 3.1.0 ou supérieur
- **URL**: https://www.nuget.org/packages/Serilog.Enrichers.Thread

### 5. Serilog.Sinks.Seq (Optionnel)
- **Package**: `Serilog.Sinks.Seq`
- **Version**: 6.0.0 ou supérieur
- **URL**: https://www.nuget.org/packages/Serilog.Sinks.Seq
- **Note**: Requis uniquement si vous utilisez Seq pour la visualisation

---

## 🔧 Méthodes d'Installation

### Méthode 1: NuGet for Unity (Recommandé)

1. **Installer NuGet for Unity**
   - Télécharger depuis: https://github.com/GlitchEnzo/NuGetForUnity
   - Ou via Unity Asset Store

2. **Ouvrir le gestionnaire NuGet**
   - Menu Unity: `NuGet > Manage NuGet Packages`

3. **Installer les packages**
   - Rechercher "Serilog"
   - Installer les 5 packages listés ci-dessus

### Méthode 2: Installation Manuelle des DLLs

1. **Télécharger les packages NuGet**
   - Aller sur https://www.nuget.org
   - Télécharger chaque package (.nupkg)

2. **Extraire les DLLs**
   - Renommer .nupkg en .zip
   - Extraire le contenu
   - Trouver les DLLs dans `/lib/netstandard2.0/` ou `/lib/net6.0/`

3. **Copier dans Unity**
   - Créer le dossier: `Assets/Plugins/Serilog/`
   - Copier toutes les DLLs dans ce dossier

4. **DLLs requises**
   ```
   Assets/Plugins/Serilog/
   ├── Serilog.dll
   ├── Serilog.Sinks.File.dll
   ├── Serilog.Formatting.Compact.dll
   ├── Serilog.Enrichers.Thread.dll
   └── Serilog.Sinks.Seq.dll (optionnel)
   ```

### Méthode 3: Via packages.config (NuGet for Unity)

Créer un fichier `packages.config` à la racine du projet Unity:

```xml
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Serilog" version="3.1.1" targetFramework="net472" />
  <package id="Serilog.Sinks.File" version="5.0.0" targetFramework="net472" />
  <package id="Serilog.Formatting.Compact" version="2.0.0" targetFramework="net472" />
  <package id="Serilog.Enrichers.Thread" version="3.1.0" targetFramework="net472" />
  <package id="Serilog.Sinks.Seq" version="6.0.0" targetFramework="net472" />
</packages>
```

Puis dans Unity: `NuGet > Restore Packages`

---

## 🧪 Vérification de l'Installation

### Test dans Unity Editor

1. Créer un script de test:

```csharp
using UnityEngine;
using Serilog;

public class SerilogTest : MonoBehaviour {
    void Start() {
        try {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File("test-log.txt")
                .CreateLogger();
            
            Log.Information("Serilog is working!");
            Log.CloseAndFlush();
            
            Debug.Log("✅ Serilog installed correctly!");
        }
        catch (System.Exception ex) {
            Debug.LogError($"❌ Serilog installation failed: {ex.Message}");
        }
    }
}
```

2. Attacher le script à un GameObject
3. Lancer la scène
4. Vérifier la console Unity pour "✅ Serilog installed correctly!"

---

## 🔍 Dépendances (Automatiques)

Ces packages seront installés automatiquement comme dépendances:

- `System.Diagnostics.DiagnosticSource`
- `System.Text.Json` (pour Seq)
- `System.Threading.Tasks.Extensions`

---

## 📋 Compatibilité

### Unity Versions
- **Minimum**: Unity 2020.3 LTS
- **Recommandé**: Unity 2021.3 LTS ou supérieur
- **Testé**: Unity 2022.3 LTS

### .NET Versions
- **.NET Standard 2.0** (Unity default)
- **.NET Framework 4.x** (Unity setting)

### Plateformes
- ✅ Windows (Standalone)
- ✅ Linux (Headless Server)
- ✅ macOS
- ⚠️ WebGL (non supporté - pas de file system)
- ⚠️ Mobile (possible mais non recommandé)

---

## ⚠️ Problèmes Courants

### Problème 1: DLL non trouvée
**Erreur**: `DllNotFoundException: Serilog`

**Solution**:
- Vérifier que les DLLs sont dans `Assets/Plugins/`
- Redémarrer Unity Editor
- Vérifier la compatibilité .NET (Project Settings > Player > API Compatibility Level)

### Problème 2: Conflit de versions
**Erreur**: `Assembly version mismatch`

**Solution**:
- Supprimer tous les packages Serilog
- Réinstaller avec les versions exactes spécifiées
- Vérifier qu'il n'y a pas de doublons dans différents dossiers

### Problème 3: Build échoue
**Erreur**: `Error building Player: Exception`

**Solution**:
- Vérifier que toutes les DLLs sont marquées pour la plateforme cible
- Dans Unity: Sélectionner chaque DLL > Inspector > Platforms
- Cocher "Standalone" et "Server"

---

## 🚀 Configuration Post-Installation

Après installation, suivre ces étapes:

1. **Vérifier l'installation**
   ```csharp
   // Test rapide
   using Serilog;
   Log.Information("Test");
   ```

2. **Initialiser le logger**
   - Ajouter `ServerLoggerInitializer` à la scène
   - Ou appeler `LoggerBootstrap.Initialize()` manuellement

3. **Configurer les variables d'environnement**
   ```batch
   SET LOG_LEVEL=Information
   SET ENVIRONMENT=Production
   ```

4. **Tester en local**
   - Lancer le serveur
   - Vérifier que `/Logs/log-YYYY-MM-DD.json` est créé

5. **Migrer le code**
   - Suivre le guide `MIGRATION_GUIDE.md`
   - Remplacer `Debug.Log` par `GameLogger.*`

---

## 📚 Ressources

- **Serilog Documentation**: https://serilog.net/
- **NuGet for Unity**: https://github.com/GlitchEnzo/NuGetForUnity
- **Seq (Log Viewer)**: https://datalust.co/seq
- **Unity Scripting Backend**: https://docs.unity3d.com/Manual/scripting-backends.html

---

## 💡 Conseils

1. **Utiliser NuGet for Unity** pour faciliter les mises à jour
2. **Versionner les DLLs** dans votre repository Git
3. **Tester en Editor** avant de build
4. **Documenter les versions** utilisées pour votre équipe
5. **Créer un build de test** avant production
