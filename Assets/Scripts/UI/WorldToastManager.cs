using System.Collections;
using System.Collections.Generic;
using Mirror;
using Sim;
using UnityEngine;

/// <summary>
/// Generic, reusable spawner for world-space toast notifications shown above a player
/// character (client-side cosmetic feedback).
///
/// LOCALITÉ — par défaut un toast est **LOCAL** : <see cref="Show"/> l'affiche au-dessus
/// du joueur local uniquement, aucun trafic réseau. C'est le cas pour la grande majorité
/// (feedback immédiat de l'action du joueur). Pour qu'un toast soit vu par les AUTRES
/// joueurs (au-dessus d'un joueur précis), le serveur broadcast un <c>S2C_WorldToast</c>
/// et chaque client appelle <see cref="ShowAbove"/> — c'est l'option opt-in.
///
///   WorldToastManager.Show("🌱 Quartier plus propre", "+1 Crédit Social ⭐", delay: 0.35f);
///   WorldToastManager.Show("Mains pleines");                       // une ligne
///   WorldToastManager.ShowAbove(netId, "...", "...");              // déclenché par S2C_WorldToast
///
/// Lazy singleton (DontDestroyOnLoad), créé à la première utilisation. Empile les toasts
/// concurrents ; chaque toast suit son ancre et se détruit seul (~2.6s).
///
/// CONVENTION (obligatoire) — tout feedback de résultat d'action passe par <see cref="ShowError"/>
/// (échec/refus : rouge + secousse + SON) ou <see cref="ShowSuccess"/> (réussite : vert + rebond,
/// SILENCIEUX). Ne jamais reconstruire un toast à la main ni utiliser <see cref="Show"/>
/// neutre pour un résultat d'action — c'est réservé aux floaties cosmétiques (emoji social via
/// <see cref="ShowAbove"/>). Côté serveur : <c>ToastNotificationMessage</c> avec <c>worldToast=true</c>
/// DOIT porter un <c>kindByte</c> (ToastKind.Error/Success), jamais Neutral. Seul l'ERREUR sonne.
/// </summary>
public class WorldToastManager : MonoBehaviour
{
    /// <summary>Default mint-green accent for the subtitle line.</summary>
    public static readonly Color DefaultAccent = new Color(0.56f, 0.92f, 0.70f, 1f);

    private const float BaseHeight   = 2.0f;   // world meters above the player pivot
    private const float StackSpacing = 0.45f;  // gap between concurrent toasts

    private static WorldToastManager _instance;
    private readonly List<WorldToast> _active = new List<WorldToast>();
    // Dédup : clé (anchor|kind|title|subtitle) → toast vivant (ou null pendant un délai en attente).
    // Tant qu'une entrée existe, un appel identique est ignoré silencieusement — évite les rafales
    // (clic maintenu sur une destination inaccessible = MoveTo 60×/s) et les doublons de codepath
    // (deux systèmes qui produisent le même message dans la même frame). L'entrée est libérée
    // quand le toast termine son animation OU si la résolution d'ancre échoue.
    private readonly Dictionary<string, WorldToast> _activeByKey = new Dictionary<string, WorldToast>();

    /// <summary>
    /// Toast LOCAL au-dessus du joueur local (aucun réseau). Cas par défaut.
    /// <paramref name="delay"/> permet de le faire suivre un autre feedback (ex. un VFX).
    /// </summary>
    public static void Show(string title, string subtitle, float delay = 0f, Color? accent = null)
        => ShowKind(0u, title, subtitle, delay, accent ?? DefaultAccent, ToastKind.Neutral);

    /// <summary>Toast LOCAL simple ligne (ex. « Mains pleines »). Pas de sous-titre accentué.</summary>
    public static void Show(string message, float delay = 0f, Color? accent = null)
        => Show(message, null, delay, accent);

    /// <summary>Toast d'ERREUR local (template commun : rouge + son + secousse).</summary>
    public static void ShowError(string message, float delay = 0f)
        => ShowKind(0u, message, null, delay, DefaultAccent, ToastKind.Error);

    /// <summary>Toast d'ERREUR local avec sous-titre.</summary>
    public static void ShowError(string title, string subtitle, float delay = 0f)
        => ShowKind(0u, title, subtitle, delay, DefaultAccent, ToastKind.Error);

    /// <summary>Toast de SUCCÈS local (template commun : vert + rebond, silencieux).</summary>
    public static void ShowSuccess(string message, float delay = 0f)
        => ShowKind(0u, message, null, delay, DefaultAccent, ToastKind.Success);

    /// <summary>Toast de SUCCÈS local avec sous-titre.</summary>
    public static void ShowSuccess(string title, string subtitle, float delay = 0f)
        => ShowKind(0u, title, subtitle, delay, DefaultAccent, ToastKind.Success);

    /// <summary>
    /// Toast au-dessus d'un joueur précis identifié par son <paramref name="anchorNetId"/>.
    /// Affiché sur CE client uniquement ; pour un rendu synchronisé chez tous les joueurs,
    /// le serveur broadcast un <c>S2C_WorldToast</c> (que chaque client relaie ici).
    /// </summary>
    public static void ShowAbove(uint anchorNetId, string title, string subtitle, float delay = 0f,
        Color? accent = null, ToastKind kind = ToastKind.Neutral)
        => ShowKind(anchorNetId, title, subtitle, delay, accent ?? DefaultAccent, kind);

    private static void ShowKind(uint anchorNetId, string title, string subtitle, float delay, Color accent, ToastKind kind)
    {
        Ensure();
        string key = MakeDedupKey(anchorNetId, title, subtitle, kind);
        // Tant qu'une entrée existe pour cette clé (toast vivant ou en attente d'un délai), on
        // ignore. On RÉSERVE la clé immédiatement (valeur null) — la coroutine la peuplera avec
        // l'instance après création.
        if (_instance._activeByKey.ContainsKey(key)) return;
        _instance._activeByKey[key] = null;
        _instance.StartCoroutine(_instance.ShowRoutine(anchorNetId, title, subtitle, delay, accent, kind, key));
    }

    private static string MakeDedupKey(uint anchorNetId, string title, string subtitle, ToastKind kind)
        => $"{anchorNetId}|{(int)kind}|{title}|{subtitle}";

    private static void Ensure()
    {
        if (_instance != null) return;
        var go = new GameObject("WorldToastManager");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<WorldToastManager>();
    }

    private IEnumerator ShowRoutine(uint anchorNetId, string title, string subtitle, float delay, Color accent, ToastKind kind, string dedupKey)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        Transform anchor = ResolveAnchor(anchorNetId);
        if (anchor == null) {
            // Pas d'ancre : on libère la clé pour qu'un appel ultérieur (joueur enfin spawné, etc.)
            // puisse passer. Sans cela, la clé resterait bloquée à jamais.
            _activeByKey.Remove(dedupKey);
            yield break;
        }

        PlayKindSound(kind);

        _active.RemoveAll(t => t == null);
        float heightOffset = BaseHeight + _active.Count * StackSpacing;

        WorldToast toast = WorldToast.Create(title, subtitle, accent, kind);
        _active.Add(toast);
        _activeByKey[dedupKey] = toast;
        toast.Play(anchor, heightOffset, () => {
            _active.Remove(toast);
            _activeByKey.Remove(dedupKey);
        });
    }

    // ── Sons par template ──────────────────────────────────────────────────────────
    // Seul le toast d'ERREUR joue un son (SfxId.UiToastError, via le catalogue audio). Les
    // toasts de succès et neutres sont silencieux : un succès est un feedback positif discret,
    // le son est réservé à ce qui doit attirer l'attention (une erreur / un refus).
    private static void PlayKindSound(ToastKind kind)
    {
        if (kind == ToastKind.Error)
            Sim.Audio.AudioManager.Instance.PlayUI(Sim.Audio.SfxId.UiToastError);
    }

    /// <summary>0 = joueur local ; sinon le joueur réseau identifié par netId (s'il est spawné ici).</summary>
    private static Transform ResolveAnchor(uint netId)
    {
        if (netId == 0)
            return PlayerController.Local != null ? PlayerController.Local.transform : null;

        if (NetworkClient.spawned != null
            && NetworkClient.spawned.TryGetValue(netId, out var identity) && identity != null)
            return identity.transform;

        return null;
    }
}
