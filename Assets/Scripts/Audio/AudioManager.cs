using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Sim.Audio {
    /// <summary>
    /// Point d'entrée unique de tous les effets sonores (hors voix Dissonance et musique de fond).
    /// Singleton auto-bootstrappé (aucun câblage de scène requis) : <see cref="Instance"/> crée le
    /// GameObject à la première utilisation, charge le <see cref="SfxCatalog"/> depuis Resources et
    /// alloue un POOL d'AudioSources réutilisables → zéro allocation par son (contrairement à
    /// AudioSource.PlayClipAtPoint), nombre de voix borné, et routage mixer centralisé.
    ///
    /// API : <see cref="Play"/> (3D positionnel), <see cref="PlayUI"/> (2D), <see cref="PlayClip2D"/>
    /// (façade legacy pour HUDManager.PlaySound). La variété et l'anti-spam sont pilotés par le
    /// catalogue (clips multiples + minInterval).
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioManager : MonoBehaviour {
        private const string CatalogResourcePath = "Audio/SfxCatalog";
        private const string MixerResourcePath   = "Audio/GameAudio";
        private const int    PoolSize = 16; // plafond de voix 3D simultanées

        private static AudioManager _instance;
        public static AudioManager Instance {
            get {
                if (_instance == null) {
                    var go = new GameObject("AudioManager");
                    _instance = go.AddComponent<AudioManager>();
                }
                return _instance;
            }
        }

        private SfxCatalog      _catalog;
        private AudioSource[]   _pool;
        private int             _poolCursor;
        private AudioSource     _uiSource;
        private AudioMixerGroup _sfxGroup;   // tous les SFX/UI passent par le groupe SFX du mixer

        /// <summary>Groupe SFX du mixer — à brancher sur les AudioSources externes (props…)
        /// pour qu'elles passent par le slider SFX. Null si aucun mixer chargé.</summary>
        public AudioMixerGroup SfxGroup => _sfxGroup;

        private readonly Dictionary<SfxId, float> _lastPlay      = new Dictionary<SfxId, float>();
        private readonly Dictionary<SfxId, int>   _lastClipIndex = new Dictionary<SfxId, int>();

        private void Awake() {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _catalog = Resources.Load<SfxCatalog>(CatalogResourcePath);
            if (_catalog != null) _catalog.Init();
            else Debug.LogWarning($"[AudioManager] SfxCatalog introuvable à Resources/{CatalogResourcePath}");

            var mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            if (mixer != null) {
                var groups = mixer.FindMatchingGroups("SFX");
                if (groups.Length > 0) _sfxGroup = groups[0];
            }

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++) {
                var src = new GameObject($"SfxSource_{i}").AddComponent<AudioSource>();
                src.transform.SetParent(transform, false);
                src.playOnAwake = false;
                src.spatialBlend = 1f;                 // 3D par défaut (ré-évalué par lecture)
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 2f;
                src.maxDistance = 22f;
                src.outputAudioMixerGroup = _sfxGroup;
                _pool[i] = src;
            }

            _uiSource = new GameObject("UiSource").AddComponent<AudioSource>();
            _uiSource.transform.SetParent(transform, false);
            _uiSource.playOnAwake = false;
            _uiSource.spatialBlend = 0f;               // 2D
            _uiSource.outputAudioMixerGroup = _sfxGroup;
        }

        /// <summary>Joue un SFX en 3D à la position donnée (pas, props, ramassage…).</summary>
        public void Play(SfxId id, Vector3 position) {
            if (!TryResolve(id, out SfxEntry entry, out AudioClip clip)) return;
            // Un son basse priorité (pas…) ne vole PAS de voix : s'il n'y a aucune source libre,
            // on le saute plutôt que de couper un son plus important.
            AudioSource src = NextSource(allowSteal: !entry.lowPriority);
            if (src == null) return;
            src.transform.position = position;
            src.spatialBlend = entry.spatial ? 1f : 0f;
            src.clip = clip;
            src.volume = entry.RandomVolume();
            src.pitch = entry.RandomPitch();
            src.priority = entry.lowPriority ? 230 : 128; // 0=prioritaire, 256=sacrifiable en premier
            src.outputAudioMixerGroup = entry.MixerGroup != null ? entry.MixerGroup : _sfxGroup;
            src.Play();
        }

        /// <summary>Joue un SFX en 2D (UI : clic, toast, notif…).</summary>
        public void PlayUI(SfxId id) {
            if (!TryResolve(id, out SfxEntry entry, out AudioClip clip)) return;
            _uiSource.outputAudioMixerGroup = entry.MixerGroup != null ? entry.MixerGroup : _sfxGroup;
            _uiSource.pitch = entry.RandomPitch();
            _uiSource.PlayOneShot(clip, entry.RandomVolume());
        }

        /// <summary>Joue un clip arbitraire en 3D à une position (overrides par-conteneur :
        /// frigo, carton…). Passe par le pool + bus SFX, comme <see cref="Play"/>.</summary>
        public void PlayClip3D(AudioClip clip, Vector3 position, float volume = 1f) {
            if (clip == null) return;
            AudioSource src = NextSource(allowSteal: true);
            if (src == null) return;
            src.transform.position = position;
            src.spatialBlend = 1f;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = 1f;
            src.priority = 128;
            src.outputAudioMixerGroup = _sfxGroup;
            src.Play();
        }

        /// <summary>
        /// Façade pour l'ancien <c>HUDManager.PlaySound(clip, volume)</c> : joue un clip arbitraire
        /// en 2D via la source mutualisée. Permet à tous les appels existants de bénéficier du
        /// pooling/mixer sans changer un seul site d'appel.
        /// </summary>
        public void PlayClip2D(AudioClip clip, float volume) {
            if (clip == null) return;
            _uiSource.pitch = 1f;
            _uiSource.outputAudioMixerGroup = _sfxGroup;
            _uiSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        // ── Internes ───────────────────────────────────────────────────────────────

        private bool TryResolve(SfxId id, out SfxEntry entry, out AudioClip clip) {
            entry = null; clip = null;
            if (_catalog == null) return false;
            entry = _catalog.Get(id);
            if (entry == null || !entry.HasClips) return false;

            if (entry.minInterval > 0f) {
                float now = Time.unscaledTime;
                if (_lastPlay.TryGetValue(id, out float last) && now - last < entry.minInterval) return false;
                _lastPlay[id] = now;
            }

            clip = PickClip(id, entry);
            return clip != null;
        }

        /// <summary>Tire un clip parmi les variations, en évitant la répétition immédiate.</summary>
        private AudioClip PickClip(SfxId id, SfxEntry entry) {
            if (entry.clips.Length == 1) return entry.clips[0];
            int prev = _lastClipIndex.TryGetValue(id, out int p) ? p : -1;
            int idx = Random.Range(0, entry.clips.Length);
            if (idx == prev) idx = (idx + 1) % entry.clips.Length;
            _lastClipIndex[id] = idx;
            return entry.clips[idx];
        }

        /// <summary>Source libre en priorité. Si aucune n'est libre : round-robin (vole la plus
        /// ancienne) seulement si <paramref name="allowSteal"/> ; sinon renvoie null.</summary>
        private AudioSource NextSource(bool allowSteal) {
            for (int i = 0; i < _pool.Length; i++) {
                int idx = (_poolCursor + i) % _pool.Length;
                if (!_pool[idx].isPlaying) { _poolCursor = (idx + 1) % _pool.Length; return _pool[idx]; }
            }
            if (!allowSteal) return null;
            AudioSource s = _pool[_poolCursor];
            _poolCursor = (_poolCursor + 1) % _pool.Length;
            return s;
        }
    }
}
