using System.Collections;
using System.Collections.Generic;
using Sim;
using UnityEngine;

/// <summary>
/// Generic, reusable spawner for local world-space toast notifications shown above the
/// local player character (client-side cosmetic feedback only). Use it for any rewarding
/// action — trash thrown, XP gained, social credit earned, etc.
///
///   WorldToastManager.Show("🌱 Quartier plus propre", "+1 Crédit Social ⭐", delay: 0.35f);
///   WorldToastManager.Show("Mission terminée", "+10 XP", accent: someColor);
///
/// Lazy singleton (DontDestroyOnLoad), created on first use. Stacks concurrent toasts so
/// they don't overlap; each toast follows the player and self-destroys after ~1.2s.
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
    /// Shows a toast above the local player. <paramref name="delay"/> lets it trail another
    /// feedback (e.g. a VFX). <paramref name="accent"/> defaults to mint green.
    /// </summary>
    public static void Show(string title, string subtitle, float delay = 0f, Color? accent = null)
    {
        Ensure();
        _instance.StartCoroutine(_instance.ShowRoutine(title, subtitle, delay, accent ?? DefaultAccent));
    }

    private static void Ensure()
    {
        if (_instance != null) return;
        var go = new GameObject("WorldToastManager");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<WorldToastManager>();
    }

    private IEnumerator ShowRoutine(string title, string subtitle, float delay, Color accent)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        PlayerController local = PlayerController.Local;
        if (local == null) yield break;

        _active.RemoveAll(t => t == null);
        float heightOffset = BaseHeight + _active.Count * StackSpacing;

        WorldToast toast = WorldToast.Create(title, subtitle, accent);
        _active.Add(toast);
        toast.Play(local.transform, heightOffset, () => _active.Remove(toast));
    }
}
