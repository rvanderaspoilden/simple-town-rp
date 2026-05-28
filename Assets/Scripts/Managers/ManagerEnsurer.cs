using Mirror;
using Sim;
using UnityEngine;

/// <summary>
/// Garantit l'existence des managers cœur (Api / Database / Network) en les instanciant
/// depuis <c>Resources/Prefabs/Managers/</c> s'ils sont absents.
///
/// **Check-before-instantiate obligatoire** : on n'instancie que si le singleton est null.
/// Ré-instancier un doublon est dangereux — ex. <c>DatabaseManager.Awake</c> ré-exécute
/// <c>RegisterPrefabs()</c> même quand il se détruit (pas de <c>return</c> après Destroy).
///
/// Utilisé par <see cref="CitySceneBootstrap"/> (auto-heal sur City) et par DevQuickPlay
/// (lancement depuis n'importe quelle scène en éditeur). Voir Docs/SCENE_BOOTSTRAP.md.
/// </summary>
public static class ManagerEnsurer
{
    public static void EnsureCoreManagers()
    {
        // NetworkManager AVANT DatabaseManager : DatabaseManager.Awake → RegisterPrefabs()
        // accède à NetworkManager.singleton.spawnPrefabs. Si on instancie DatabaseManager
        // d'abord (NetworkManager.singleton null), RegisterPrefabs lève une NRE qui remonte
        // dans Instantiate → EnsureCoreManagers s'interrompt → NetworkManager jamais créé →
        // serveur jamais démarré (plus de missions, etc.).
        if (ApiManager.Instance == null)      Ensure("Prefabs/Managers/Api Manager",     "Api Manager");
        if (NetworkManager.singleton == null) Ensure("Prefabs/Managers/Network Manager",  "Network Manager");
        if (DatabaseManager.Instance == null) Ensure("Prefabs/Managers/DatabaseManager",  "DatabaseManager");
    }

    private static void Ensure(string resourcePath, string label)
    {
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogError($"[ManagerEnsurer] Prefab introuvable : Resources/{resourcePath}");
            return;
        }
        GameObject go = Object.Instantiate(prefab);
        go.name = prefab.name; // enlève le suffixe "(Clone)"
        Debug.Log($"[ManagerEnsurer] '{label}' absent → instancié depuis Resources/{resourcePath}.");
    }
}
