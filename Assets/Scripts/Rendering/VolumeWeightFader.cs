using UnityEngine;
using UnityEngine.Rendering;

namespace Sim {
    /// <summary>
    /// Volume GLOBAL de session (profil partagé, priorité donnée) dont le poids est FONDU vers une
    /// cible. Autonome : vit au niveau scène, survit donc à la désactivation de l'appelant (un
    /// panneau HUD qui se ferme) et se détruit tout seul quand un fondu vers 0 l'a demandé.
    /// Créé sur le layer Default — le seul que la caméra principale écoute (Volume Mask du
    /// Camera Manager). Utilisé par la cabine d'essayage (<c>FittingRoomUI</c>).
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public class VolumeWeightFader : MonoBehaviour {
        private Volume _volume;
        private float _target;
        private float _speed; // poids / seconde
        private bool _destroyAtZero;

        /// <summary>Volume global au poids 0, prêt à être fondu via <see cref="FadeTo"/>.</summary>
        public static VolumeWeightFader Spawn(string name, VolumeProfile profile, float priority) {
            var go = new GameObject(name);
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            volume.sharedProfile = profile;
            volume.weight = 0f;

            var fader = go.AddComponent<VolumeWeightFader>();
            fader._volume = volume;
            fader.enabled = false;
            return fader;
        }

        /// <summary>Fond le poids vers <paramref name="target"/> en <paramref name="duration"/> s
        /// (0 = immédiat). <paramref name="destroyAtZero"/> : l'objet se détruit une fois à 0.</summary>
        public void FadeTo(float target, float duration, bool destroyAtZero = false) {
            this._target = Mathf.Clamp01(target);
            this._speed = duration > 0f ? 1f / duration : float.PositiveInfinity;
            this._destroyAtZero = destroyAtZero;
            this.enabled = true;
        }

        private void Update() {
            if (this._volume == null) {
                Destroy(this.gameObject);
                return;
            }

            this._volume.weight = Mathf.MoveTowards(this._volume.weight, this._target, this._speed * Time.unscaledDeltaTime);
            if (!Mathf.Approximately(this._volume.weight, this._target)) return;

            if (this._destroyAtZero && this._target <= 0f) Destroy(this.gameObject);
            else this.enabled = false;
        }
    }
}
