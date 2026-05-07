using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registre plain C# (singleton) des points d'intérêt actifs dans la scène.
/// Les <see cref="InterestPoint"/> s'enregistrent automatiquement via OnEnable/OnDisable.
/// Côté IA, les NPC piochent un point via PickRandom (avec exclusion optionnelle).
/// </summary>
public class InterestPointRegistry {
    private static InterestPointRegistry _instance;
    public static  InterestPointRegistry Instance => _instance ??= new InterestPointRegistry();

    private readonly List<InterestPoint> _points = new List<InterestPoint>();

    public IReadOnlyList<InterestPoint> All => _points;

    public void Register(InterestPoint point) {
        if (point == null || _points.Contains(point)) return;
        _points.Add(point);
    }

    public void Unregister(InterestPoint point) {
        if (point == null) return;
        _points.Remove(point);
    }

    /// <summary>
    /// Retourne un point d'intérêt aléatoire actif, en excluant éventuellement <paramref name="exclude"/>.
    /// Retourne null si aucun candidat.
    /// </summary>
    public InterestPoint PickRandom(InterestPoint exclude = null) {
        // Comptage filtré pour éviter une allocation
        int count = 0;
        for (int i = 0; i < _points.Count; i++) {
            if (_points[i] != null && _points[i].IsActive && _points[i] != exclude) count++;
        }
        if (count == 0) return null;

        int target = Random.Range(0, count);
        int seen = 0;
        for (int i = 0; i < _points.Count; i++) {
            var p = _points[i];
            if (p == null || !p.IsActive || p == exclude) continue;
            if (seen == target) return p;
            seen++;
        }
        return null;
    }

    public void Reset() => _points.Clear();
}
