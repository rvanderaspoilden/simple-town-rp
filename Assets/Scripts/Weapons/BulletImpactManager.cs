using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Sim {
    /// <summary>
    /// Impacts de balle, purement visuels et locaux à chaque client : projette un décal
    /// « trou de balle » (URP DecalProjector) sur la surface touchée, orienté sur la normale
    /// d'impact avec un roll aléatoire et une petite variation de taille. Pool circulaire
    /// (MaxDecals) — le plus vieux décal est recyclé — avec fondu (fadeFactor) sur les
    /// dernières secondes de vie. Appelé par PlayerShooter.RpcFired quand le tir touche une
    /// surface (jamais sur un joueur).
    ///
    /// Matériau : Resources/Weapons/BulletHole.mat (shader Decal URP). Nécessite la
    /// Decal Renderer Feature sur le renderer URP — sans elle, les décals sont invisibles.
    ///
    /// Singleton paresseux DontDestroyOnLoad, créé par code (aucun prefab à éditer).
    /// </summary>
    public class BulletImpactManager : MonoBehaviour {
        private const int MaxDecals = 64;
        private const float Lifetime = 40f;
        private const float FadeDuration = 5f;
        private const float MinSize = 0.07f;
        private const float MaxSize = 0.12f;
        private const float ProjectionDepth = 0.15f;
        // Recul du projecteur au-dessus de la surface : le volume de projection (depth)
        // chevauche ainsi la géométrie même légèrement irrégulière.
        private const float SurfaceOffset = 0.05f;
        private const string MaterialResourcePath = "Weapons/BulletHole";

        private static BulletImpactManager _instance;

        public static BulletImpactManager Instance {
            get {
                if (_instance == null) {
                    var go = new GameObject(nameof(BulletImpactManager));
                    _instance = go.AddComponent<BulletImpactManager>();
                }
                return _instance;
            }
        }

        private Material _material;
        private bool _materialMissingLogged;

        private struct Entry {
            public DecalProjector Projector;
            public float DieAt;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _nextSlot;

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(this.gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(this.gameObject);
            this._material = Resources.Load<Material>(MaterialResourcePath);
        }

        /// <summary>Projette un trou de balle à <paramref name="point"/> sur la surface de
        /// normale <paramref name="normal"/>.</summary>
        public void Spawn(Vector3 point, Vector3 normal) {
            if (this._material == null) {
                this._material = Resources.Load<Material>(MaterialResourcePath);
                if (this._material == null) {
                    if (!this._materialMissingLogged) {
                        Debug.LogWarning($"[BulletImpactManager] Matériau décal introuvable: Resources/{MaterialResourcePath}");
                        this._materialMissingLogged = true;
                    }
                    return;
                }
            }
            if (normal.sqrMagnitude < 0.5f) return;
            normal.Normalize();

            DecalProjector projector = this.NextProjector();

            // Le projecteur projette le long de son +Z : on le pose un peu AU-DESSUS de la
            // surface, +Z vers elle, roll aléatoire autour de la normale pour varier les trous.
            Vector3 up = Mathf.Abs(normal.y) > 0.99f ? Vector3.forward : Vector3.up;
            Quaternion rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), normal)
                                  * Quaternion.LookRotation(-normal, up);

            float size = Random.Range(MinSize, MaxSize);
            projector.transform.SetPositionAndRotation(point + normal * SurfaceOffset, rotation);
            projector.size = new Vector3(size, size, ProjectionDepth);
            projector.pivot = new Vector3(0f, 0f, ProjectionDepth * 0.5f);
            projector.fadeFactor = 1f;
            projector.gameObject.SetActive(true);

            int slot = this.SlotOf(projector);
            if (slot >= 0) {
                Entry entry = this._entries[slot];
                entry.DieAt = Time.time + Lifetime;
                this._entries[slot] = entry;
            }
        }

        /// <summary>Slot libre ou le plus vieux (pool circulaire).</summary>
        private DecalProjector NextProjector() {
            if (this._entries.Count < MaxDecals) {
                var go = new GameObject($"BulletHole {this._entries.Count}");
                go.transform.SetParent(this.transform, false);
                var projector = go.AddComponent<DecalProjector>();
                projector.material = this._material;
                go.SetActive(false);
                this._entries.Add(new Entry { Projector = projector, DieAt = 0f });
                return projector;
            }

            DecalProjector reused = this._entries[this._nextSlot].Projector;
            this._nextSlot = (this._nextSlot + 1) % MaxDecals;
            return reused;
        }

        private int SlotOf(DecalProjector projector) {
            for (int i = 0; i < this._entries.Count; i++) {
                if (this._entries[i].Projector == projector) return i;
            }
            return -1;
        }

        private void Update() {
            for (int i = 0; i < this._entries.Count; i++) {
                Entry entry = this._entries[i];
                if (entry.Projector == null || !entry.Projector.gameObject.activeSelf) continue;

                float remaining = entry.DieAt - Time.time;
                if (remaining <= 0f) {
                    entry.Projector.gameObject.SetActive(false);
                } else if (remaining < FadeDuration) {
                    entry.Projector.fadeFactor = remaining / FadeDuration;
                }
            }
        }
    }
}
