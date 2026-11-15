#if UNITY_EDITOR
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using kcp2k;
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

    // Port UDP réellement utilisé par l'hôte Quick Play. Partagé via EditorPrefs (registre HKCU,
    // commun à toutes les instances d'éditeur de la machine) pour qu'un clone ParrelSync rejoigne
    // le bon port si l'hôte a dû se replier (voir EnsureFreeHostPort).
    private const string PortPrefKey = "SimpleTown.QuickPlay.Port";
    private const int PortProbeRange = 10;

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
        if (scene.name == "LootOverlayTest") return;                        // harnais de test UI (hors ligne)
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
            ApplyHostPortToClone(nm);
            Debug.Log($"[DevQuickPlay] Clone ParrelSync détecté → StartClient (rejoint l'hôte). Compte={account}");
            nm.StartClient();
        }
        else
        {
            EnsureFreeHostPort(nm);
            Debug.Log($"[DevQuickPlay] StartHost (lancement + auto-connexion). Compte={account}");
            nm.StartHost();
        }
    }

    // ── Port UDP : repli automatique si le port configuré est occupé ─────────────
    // Une socket KCP orpheline (play mode interrompu par une exception dans le teardown, éditeur
    // tué…) peut rester bloquée sur 7777 dans le process éditeur jusqu'à son redémarrage. Plutôt
    // que d'échouer avec « Une seule utilisation de chaque adresse de socket… », on sonde le port
    // avec la même configuration que kcp2k et on glisse vers le premier port libre suivant.
    private static void EnsureFreeHostPort(NetworkManager nm)
    {
        if (!(nm.transport is PortTransport portTransport)) return;
        bool dualMode = !(nm.transport is KcpTransport kcp) || kcp.DualMode;

        ushort basePort = portTransport.Port;
        for (int offset = 0; offset < PortProbeRange; offset++)
        {
            ushort candidate = (ushort)(basePort + offset);
            if (!IsUdpPortFree(candidate, dualMode)) continue;

            if (candidate != basePort)
            {
                Debug.LogWarning($"[DevQuickPlay] Port UDP {basePort} occupé (socket orpheline d'une session Play précédente ?) → hôte sur {candidate}. Un clone ParrelSync en Quick Play suivra automatiquement.");
                portTransport.Port = candidate;
            }
            EditorPrefs.SetInt(PortPrefKey, candidate);
            return;
        }

        Debug.LogError($"[DevQuickPlay] Aucun port UDP libre entre {basePort} et {basePort + PortProbeRange - 1} — StartHost va échouer. Redémarrer l'éditeur libère les sockets orphelines.");
    }

    /// <summary>Clone ParrelSync : vise le port sur lequel l'hôte Quick Play a réellement démarré.</summary>
    private static void ApplyHostPortToClone(NetworkManager nm)
    {
        if (!(nm.transport is PortTransport portTransport)) return;
        int hostPort = EditorPrefs.GetInt(PortPrefKey, 0);
        if (hostPort <= 0 || hostPort > ushort.MaxValue || hostPort == portTransport.Port) return;

        Debug.Log($"[DevQuickPlay] L'hôte Quick Play écoute sur {hostPort} (repli) → le clone se connecte sur ce port.");
        portTransport.Port = (ushort)hostPort;
    }

    /// <summary>Sonde de bind identique à kcp2k.KcpServer.CreateServerSocket (IPv6 dual-mode ou IPv4).</summary>
    private static bool IsUdpPortFree(ushort port, bool dualMode)
    {
        Socket socket = null;
        try
        {
            if (dualMode)
            {
                socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            }
            else
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            socket?.Close();
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
