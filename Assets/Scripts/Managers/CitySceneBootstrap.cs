using UnityEngine;

/// <summary>
/// Auto-heal des managers DontDestroyOnLoad : garantit qu'Api Manager, DatabaseManager et
/// Network Manager existent même si la scène est lancée en standalone (Play directement sur
/// City) sans passer par Launcher.
///
/// Dans le flux normal (Launcher → City), ces singletons DDOL existent déjà → no-op. Cela
/// permet à City de ne garder AUCUNE copie posée à la main de ces managers : on ne peut donc
/// plus les supprimer par erreur de la scène.
///
/// S'exécute très tôt (DefaultExecutionOrder très négatif) pour que les singletons soient
/// prêts avant les autres Awake. PropSystem est auto-créé par PropSystemBootstrap.
/// Délègue la logique à <see cref="ManagerEnsurer"/> (partagée avec DevQuickPlay).
/// Voir Docs/SCENE_BOOTSTRAP.md.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class CitySceneBootstrap : MonoBehaviour
{
    private void Awake()
    {
        ManagerEnsurer.EnsureCoreManagers();
    }
}
