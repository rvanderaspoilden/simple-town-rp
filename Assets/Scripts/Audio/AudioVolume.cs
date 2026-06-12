using UnityEngine;
using UnityEngine.Audio;
using Sim.Entities;

namespace Sim.Audio {
    /// <summary>
    /// Applique les volumes joueur (Master / Musique / SFX) au système audio :
    /// - Master → <see cref="AudioListener.volume"/> (couvre TOUT, y compris la voix et les
    ///   sources non encore routées dans le mixer) ;
    /// - Musique → paramètre exposé "MusicVol" du mixer (groupe Music) ;
    /// - SFX → paramètre exposé "SfxVol" du mixer (groupe SFX).
    ///
    /// Conversion linéaire (slider 0..1) → décibels. S'applique automatiquement au chargement
    /// des réglages utilisateur (démarrage) et en live depuis l'écran Settings du téléphone.
    /// </summary>
    public static class AudioVolume {
        private const string MixerPath = "Audio/GameAudio";
        private static AudioMixer _mixer;
        private static AudioMixer Mixer => _mixer != null ? _mixer : (_mixer = Resources.Load<AudioMixer>(MixerPath));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook() {
            // Appliqué dès que les réglages utilisateur sont chargés depuis le backend.
            ApiManager.OnUserSettingsLoaded += s => { if (s != null) ApplyFrom(s.Data); };
        }

        public static void ApplyFrom(UserSettingsData d) {
            if (d == null) return;
            Apply(d.AudioMaster, d.AudioMusic, d.AudioSfx);
        }

        public static void Apply(float master, float music, float sfx) {
            AudioListener.volume = Mathf.Clamp01(master);
            var m = Mixer;
            if (m == null) return;
            m.SetFloat("MusicVol", ToDecibels(music));
            m.SetFloat("SfxVol",   ToDecibels(sfx));
        }

        /// <summary>0 → -80 dB (silence), 1 → 0 dB. Échelle perceptuelle (log).</summary>
        private static float ToDecibels(float linear) {
            linear = Mathf.Clamp01(linear);
            return linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;
        }
    }
}
