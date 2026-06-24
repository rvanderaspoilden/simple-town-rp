using System.Collections.Generic;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Keeps the URP additional-light shadow atlas from overflowing WITHOUT lowering
    /// shadow resolution. Each frame-budget it ranks every shadow-casting point/spot
    /// light by distance to the local player and only lets the nearest ones (within a
    /// tile budget) keep casting; the rest have their shadows temporarily disabled and
    /// restored when they come back into range.
    ///
    /// A point light costs 6 atlas tiles (cube faces), a spot costs 1. Because the prop
    /// bulbs are now downward spots and the atlas is 8192, this culler stays dormant in
    /// normal play and only acts as a guardrail when an unusually dense cluster of
    /// shadow-casting lights would otherwise force URP to drop resolution.
    ///
    /// Self-bootstrapping: spawned via <see cref="RuntimeInitializeOnLoadMethod"/>, so no
    /// component has to be authored into any scene or prefab.
    /// </summary>
    public class AdditionalLightShadowCuller : MonoBehaviour {
        // Tile budget for the additional-light shadow atlas. Point = 6, Spot = 1.
        private const int   MaxShadowTiles = 48;
        private const float ScanInterval   = 0.5f;

        // Authored shadow setting per light, captured the first time we see it so we can
        // restore exactly what the prefab/scene specified (never upgrade None → shadows).
        private readonly Dictionary<Light, LightShadows> _authored = new Dictionary<Light, LightShadows>();
        private readonly List<Light> _candidates = new List<Light>();
        private float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() {
            GameObject go = new GameObject("[AdditionalLightShadowCuller]");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<AdditionalLightShadowCuller>();
        }

        private void Update() {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            Rebuild();
        }

        private void Rebuild() {
            Vector3 origin = ResolveOrigin();

            _candidates.Clear();
            Light[] all = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Light l in all) {
                if (l == null) continue;
                if (l.type != LightType.Point && l.type != LightType.Spot) continue;

                if (!_authored.TryGetValue(l, out LightShadows authored)) {
                    authored = l.shadows;
                    _authored[l] = authored;
                }
                if (authored == LightShadows.None) continue; // never wanted shadows
                _candidates.Add(l);
            }

            _candidates.Sort((a, b) =>
                (a.transform.position - origin).sqrMagnitude
                .CompareTo((b.transform.position - origin).sqrMagnitude));

            int tiles = 0;
            foreach (Light l in _candidates) {
                int cost = l.type == LightType.Point ? 6 : 1;
                LightShadows desired = (tiles + cost <= MaxShadowTiles) ? _authored[l] : LightShadows.None;
                if (desired != LightShadows.None) tiles += cost;
                if (l.shadows != desired) l.shadows = desired;
            }

            PruneDead();
        }

        private static Vector3 ResolveOrigin() {
            PlayerController p = PlayerController.Local;
            if (p != null) return p.transform.position;
            Camera cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        private void PruneDead() {
            if (_authored.Count < 256) return; // amortize: only sweep when it grows
            List<Light> dead = null;
            foreach (KeyValuePair<Light, LightShadows> kv in _authored) {
                if (kv.Key == null) (dead ??= new List<Light>()).Add(kv.Key);
            }
            if (dead != null) foreach (Light l in dead) _authored.Remove(l);
        }
    }
}
