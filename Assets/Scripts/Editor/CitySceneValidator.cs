#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Garde-fou éditeur contre la suppression accidentelle d'objets critiques de la scène City.
///
/// À chaque sauvegarde de City (et via le menu Tools ▸ Simple Town ▸ Validate City Scene),
/// on vérifie que tous les composants requis sont présents quelque part dans la scène.
/// S'il en manque, une ERREUR rouge liste précisément lesquels — impossible de sauvegarder
/// « en ayant oublié » sans s'en rendre compte.
///
/// On valide par TYPE de composant (pas par nom de GameObject) : robuste aux renommages,
/// et reflète la vraie perte fonctionnelle.
///
/// NB : on NE liste PAS ApiManager / DatabaseManager / SimpleTownNetwork / ClientPropManager :
/// ce sont des singletons DontDestroyOnLoad bootstrappés par la scène Launcher (doublons en
/// City). Le bootstrap runtime les recrée si besoin — voir Docs/SCENE_BOOTSTRAP.md.
/// </summary>
[InitializeOnLoad]
public static class CitySceneValidator
{
    private const string CitySceneName = "City";

    // Composants spécifiques à City, NON auto-réparables ou requis pour le gameplay.
    // Matchés par Type.Name (nom de classe court).
    private static readonly string[] RequiredComponents =
    {
        // Objets réseau de scène (NetworkIdentity) — impossibles à recréer par bootstrap.
        "TimeManager",
        "SubGameController",
        "CityRoomInitializer",
        // Système de bâtiments (Behaviour Manager, livré inactif).
        "BuildingBehavior",
        // Input UI.
        "EventSystem",
        // Auto-heal des managers DDOL (Api/Database/Network) en standalone.
        "CitySceneBootstrap",
        // Managers DDOL propres au gameplay (pas bootstrappés par Launcher).
        "HUDManager",
        "CameraManager",
        "CursorManager",
        "LoadingManager",
        "MarkerController",
        "MissionHighlightManager",
    };

    static CitySceneValidator()
    {
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    private static void OnSceneSaving(Scene scene, string path)
    {
        if (scene.name == CitySceneName)
            Validate(scene, viaSave: true);
    }

    [MenuItem("Tools/Simple Town/Validate City Scene")]
    private static void ValidateActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != CitySceneName)
        {
            Debug.LogWarning($"[CitySceneValidator] La scène active n'est pas '{CitySceneName}' (c'est '{scene.name}'). Validation ignorée.");
            return;
        }
        if (Validate(scene, viaSave: false))
            Debug.Log("[CitySceneValidator] ✅ City : tous les objets requis sont présents.");
    }

    /// <returns>true si tout est présent, false s'il manque quelque chose.</returns>
    private static bool Validate(Scene scene, bool viaSave)
    {
        HashSet<string> present = new HashSet<string>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component c in root.GetComponentsInChildren<Component>(includeInactive: true))
                if (c != null)
                    present.Add(c.GetType().Name);

        List<string> missing = RequiredComponents.Where(r => !present.Contains(r)).ToList();
        if (missing.Count == 0)
            return true;

        string ctx = viaSave ? " (déclenché à la sauvegarde)" : "";
        Debug.LogError(
            $"[CitySceneValidator]{ctx} ⚠️ Objets REQUIS manquants dans la scène City : "
            + $"{string.Join(", ", missing)}.\n"
            + "Un GameObject critique a probablement été supprimé. Restaure-le (prefab / undo) "
            + "avant de continuer — voir Docs/SCENE_BOOTSTRAP.md.");
        return false;
    }
}
#endif
