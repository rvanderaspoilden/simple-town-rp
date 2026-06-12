using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Sim.Audio {
    /// <summary>
    /// Une entrée de catalogue : un <see cref="SfxId"/> → un ou plusieurs clips (variations),
    /// avec ses plages de volume/pitch, sa spatialisation (2D UI vs 3D monde), son groupe de
    /// mixer et un intervalle anti-spam. La variété vient de <see cref="clips"/> (tirage
    /// aléatoire sans répétition immédiate, géré par l'AudioManager).
    /// </summary>
    [System.Serializable]
    public class SfxEntry {
        public SfxId id;
        public AudioClip[] clips;

        [Tooltip("Plage de volume (x=min, y=max), tirée aléatoirement à chaque lecture.")]
        public Vector2 volume = new Vector2(1f, 1f);
        [Tooltip("Plage de pitch (x=min, y=max), tirée aléatoirement à chaque lecture.")]
        public Vector2 pitch = new Vector2(1f, 1f);

        [Tooltip("3D = son positionnel dans le monde (pas, props…). Décoché = 2D (UI).")]
        public bool spatial = true;
        [Tooltip("Intervalle minimal (s) entre deux lectures de ce SfxId (anti-spam). 0 = aucun.")]
        public float minInterval = 0f;
        [Tooltip("Son d'ambiance secondaire (pas, etc.) : basse priorité (Unity le coupe en premier " +
                 "si trop de voix) et ne vole jamais une source du pool aux autres sons.")]
        public bool lowPriority = false;
        [Tooltip("Groupe de mixer optionnel (Master/SFX/UI/Ambient…). Null = sortie par défaut.")]
        public AudioMixerGroup mixerGroup;

        public AudioMixerGroup MixerGroup => mixerGroup;
        public bool HasClips => clips != null && clips.Length > 0;
        public float RandomVolume() => Random.Range(Mathf.Min(volume.x, volume.y), Mathf.Max(volume.x, volume.y));
        public float RandomPitch()  => Random.Range(Mathf.Min(pitch.x, pitch.y), Mathf.Max(pitch.x, pitch.y));
    }

    /// <summary>
    /// Catalogue centralisé des effets sonores, data-driven. Chargé par l'AudioManager depuis
    /// Resources/Audio/SfxCatalog. Source unique de vérité : ajouter/changer/varier un son se
    /// fait ici, sans toucher au code. Un SfxId sans clips est un no-op silencieux (sons à venir).
    /// </summary>
    [CreateAssetMenu(fileName = "SfxCatalog", menuName = "Configurations/Audio/Sfx Catalog")]
    public class SfxCatalog : ScriptableObject {
        [SerializeField] private List<SfxEntry> entries = new List<SfxEntry>();

        private Dictionary<SfxId, SfxEntry> _map;

        public void Init() {
            _map = new Dictionary<SfxId, SfxEntry>(entries.Count);
            foreach (var e in entries) {
                if (e == null || e.id == SfxId.None) continue;
                _map[e.id] = e; // dernière entrée gagne en cas de doublon
            }
        }

        public SfxEntry Get(SfxId id) {
            if (_map == null) Init();
            return _map.TryGetValue(id, out var e) ? e : null;
        }
    }
}
