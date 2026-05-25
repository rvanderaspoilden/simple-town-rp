#if UNITY_EDITOR
using System;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Confort dev (ÉDITEUR UNIQUEMENT) : permet de presser Play depuis n'importe quelle scène
/// (City, sub-games…) hors Launcher pour lancer le jeu et s'auto-connecter, sans repasser par
/// Launcher + Host du HUD Mirror.
///
/// Activable via le menu <b>Tools ▸ Simple Town ▸ Quick Play</b> (coché = actif, stocké en
/// EditorPrefs). Quand actif et qu'on entre en Play sur une scène gameplay :
///   1. garantit les managers cœur (ManagerEnsurer) ;
///   2. force le compte dev choisi (Spectus par défaut, ou Elbloody) via EditorSetDevAccount ;
///   3. StartHost (ou StartClient si on est un clone ParrelSync → test 2 joueurs en un clic).
/// Mirror charge alors onlineScene = City automatiquement.
///
/// Tout le fichier est sous #if UNITY_EDITOR → strippé des builds (aucun impact runtime).
/// Compte par défaut : Spectus (sélectionnable via le sous-menu Quick Play Account).
/// Voir Docs/SCENE_BOOTSTRAP.md.
/// </summary>
public static class DevQuickPlay
{
    private const string MenuPath = "Tools/Simple Town/Quick Play (auto-host depuis toute scène)";
    private const string PrefKey  = "SimpleTown.QuickPlay";

    // Compte dev utilisé pour l'auto-connexion. true = Spectus (défaut), false = Elbloody.
    private const string AccountSpectusMenu  = "Tools/Simple Town/Quick Play Account/Spectus";
    private const string AccountElbloodyMenu = "Tools/Simple Town/Quick Play Account/Elbloody";
    private const string AccountPrefKey      = "SimpleTown.QuickPlaySpectus";

    private static bool UseSpectus => EditorPrefs.GetBool(AccountPrefKey, true);

    // ── Menu toggle ─────────────────────────────────────────────────────────────
    [MenuItem(MenuPath, false, 100)]
    private static void Toggle() => EditorPrefs.SetBool(PrefKey, !EditorPrefs.GetBool(PrefKey, false));

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PrefKey, false));
        return true;
    }

    // ── Choix du compte (radio) ──────────────────────────────────────────────────
    [MenuItem(AccountSpectusMenu, false, 120)]
    private static void SelectSpectus() => EditorPrefs.SetBool(AccountPrefKey, true);

    [MenuItem(AccountSpectusMenu, true)]
    private static bool SelectSpectusValidate()
    {
        Menu.SetChecked(AccountSpectusMenu, UseSpectus);
        return true;
    }

    [MenuItem(AccountElbloodyMenu, false, 121)]
    private static void SelectElbloody() => EditorPrefs.SetBool(AccountPrefKey, false);

    [MenuItem(AccountElbloodyMenu, true)]
    private static bool SelectElbloodyValidate()
    {
        Menu.SetChecked(AccountElbloodyMenu, !UseSpectus);
        return true;
    }

    // ── Auto-start (une seule fois par session Play) ─────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.name == "Launcher" || scene.name == "Main Menu") return; // flux normal
        if (NetworkServer.active || NetworkClient.active) return;           // déjà lancé

        ManagerEnsurer.EnsureCoreManagers();

        SimpleTownNetwork nm = NetworkManager.singleton as SimpleTownNetwork;
        if (nm == null)
        {
            Debug.LogError("[DevQuickPlay] Aucun SimpleTownNetwork après EnsureCoreManagers — abandon.");
            return;
        }

        bool spectus = UseSpectus;
        nm.EditorSetDevAccount(spectus);
        string account = spectus ? "Spectus" : "Elbloody";

        if (IsParrelSyncClone())
        {
            Debug.Log($"[DevQuickPlay] Clone ParrelSync détecté → StartClient (rejoint l'hôte). Compte={account}");
            nm.StartClient();
        }
        else
        {
            Debug.Log($"[DevQuickPlay] StartHost (lancement + auto-connexion). Compte={account}");
            nm.StartHost();
        }
    }

    /// <summary>
    /// Détecte un clone ParrelSync par réflexion (pas de dépendance de compilation : si
    /// ParrelSync est absent, on retombe sur StartHost).
    /// </summary>
    private static bool IsParrelSyncClone()
    {
        try
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType("ParrelSync.ClonesManager");
                if (t == null) continue;
                MethodInfo m = t.GetMethod("IsClone", BindingFlags.Public | BindingFlags.Static);
                if (m != null) return (bool)m.Invoke(null, null);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DevQuickPlay] Détection ParrelSync échouée ({e.Message}) — StartHost par défaut.");
        }
        return false;
    }
}
#endif
