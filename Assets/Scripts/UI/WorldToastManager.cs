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
/// </summary>
public class WorldToastManager : MonoBehaviour
{
    /// <summary>Default mint-green accent for the subtitle line.</summary>
    public static readonly Color DefaultAccent = new Color(0.56f, 0.92f, 0.70f, 1f);

    private const float BaseHeight   = 2.0f;   // world meters above the player pivot
    private const float StackSpacing = 0.45f;  // gap between concurrent toasts

    private static WorldToastManager _instance;
    private readonly List<WorldToast> _active = new List<WorldToast>();

    /// <summary>
    /// Toast LOCAL au-dessus du joueur local (aucun réseau). Cas par défaut.
    /// <paramref name="delay"/> permet de le faire suivre un autre feedback (ex. un VFX).
    /// </summary>
    public static void Show(string title, string subtitle, float delay = 0f, Color? accent = null)
        => ShowKind(0u, title, subtitle, delay, accent ?? DefaultAccent, ToastKind.Neutral);

    /// <summary>Toast LOCAL simple ligne (ex. « Mains pleines »). Pas de sous-titre accentué.</summary>
    public static void Show(string message, float delay = 0f, Color? accent = null)
        => Show(message, null, delay, accent);

    /// <summary>Toast d'ERREUR local (template commun : rouge + icône ⚠ + son + secousse).</summary>
    public static void ShowError(string message, float delay = 0f)
        => ShowKind(0u, message, null, delay, DefaultAccent, ToastKind.Error);

    /// <summary>Toast d'ERREUR local avec sous-titre.</summary>
    public static void ShowError(string title, string subtitle, float delay = 0f)
        => ShowKind(0u, title, subtitle, delay, DefaultAccent, ToastKind.Error);

    /// <summary>Toast de SUCCÈS local (template commun : vert + icône ✓ + son + rebond).</summary>
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
        _instance.StartCoroutine(_instance.ShowRoutine(anchorNetId, title, subtitle, delay, accent, kind));
    }

    private static void Ensure()
    {
        if (_instance != null) return;
        var go = new GameObject("WorldToastManager");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<WorldToastManager>();
    }

    private IEnumerator ShowRoutine(uint anchorNetId, string title, string subtitle, float delay, Color accent, ToastKind kind)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        Transform anchor = ResolveAnchor(anchorNetId);
        if (anchor == null) yield break;

        PlayKindSound(kind);

        _active.RemoveAll(t => t == null);
        float heightOffset = BaseHeight + _active.Count * StackSpacing;

        WorldToast toast = WorldToast.Create(title, subtitle, accent, kind);
        _active.Add(toast);
        toast.Play(anchor, heightOffset, () => _active.Remove(toast));
    }

    // ── Sons par template ──────────────────────────────────────────────────────────
    // Joués via la source audio UI partagée du HUD. Clips chargés depuis Resources
    // Routé via le catalogue audio (SfxId.UiToastSuccess / UiToastError) : changer le son se
    // fait désormais dans le SfxCatalog, plus aucun chemin string ici. Silencieux si non câblé.
    private static void PlayKindSound(ToastKind kind)
    {
        if (kind == ToastKind.Neutral) return;
        Sim.Audio.AudioManager.Instance.PlayUI(
            kind == ToastKind.Error ? Sim.Audio.SfxId.UiToastError : Sim.Audio.SfxId.UiToastSuccess);
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
