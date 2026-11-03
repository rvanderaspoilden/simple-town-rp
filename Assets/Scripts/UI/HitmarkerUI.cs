using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Hitmarker centre-écran : flash de 4 traits en ✕ quand le SERVEUR confirme qu'un coup
    /// (balle, hache, poing) a touché un personnage — déclenché par les TargetRpc
    /// TargetDamageDealt de PlayerShooter/PlayerMelee, en même temps que le chiffre de
    /// dégâts (<see cref="DamageNumber"/>). Blanc = touché, orange = cible mise KO.
    ///
    /// Vit sur son propre GameObject TOUJOURS actif sous le HUD (structure autorée dans le
    /// prefab HUD Manager), volontairement indépendant du Crosshair : celui-ci est masqué
    /// hors TPS et sous la lunette sniper (ScopeOverlayUI), or le hitmarker doit rester
    /// visible scopé. Alpha 0 au repos — invisible tant que rien n'est touché.
    /// Blanc = touché, orange = KO, rouge (+ pop plus fort) = headshot.
    /// </summary>
    public class HitmarkerUI : MonoBehaviour {
        [Tooltip("Groupe des traits — alpha piloté par Notify (0 au repos).")]
        [SerializeField]
        private CanvasGroup group;

        [Tooltip("Traits du hitmarker — teintés ensemble à chaque coup confirmé.")]
        [SerializeField]
        private Image[] strokes;

        [Tooltip("Teinte quand le coup touche sans mettre KO.")]
        [SerializeField]
        private Color hitColor = new Color(1f, 1f, 1f, 0.95f);

        [Tooltip("Teinte quand le coup met la cible KO.")]
        [SerializeField]
        private Color knockColor = new Color(1f, 0.62f, 0.25f, 1f);

        [Tooltip("Teinte d'un headshot (tir à la tête) — rouge vif, bien distinct du coup normal.")]
        [SerializeField]
        private Color headshotColor = new Color(1f, 0.15f, 0.12f, 1f);

        [Tooltip("Durée d'affichage (s) après un coup confirmé.")]
        [SerializeField]
        private float duration = 0.25f;

        [Tooltip("Pop d'échelle appliqué au flash — un headshot pop plus fort qu'un coup normal.")]
        [SerializeField]
        private float hitPopScale = 1.35f;

        [Tooltip("Pop d'échelle d'un headshot — plus marqué pour que le coup à la tête se sente.")]
        [SerializeField]
        private float headshotPopScale = 1.9f;

        public static HitmarkerUI Instance;

        private float visibleUntil;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
            if (this.group != null) this.group.alpha = 0f;
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        /// <summary>Flash du hitmarker : teinte selon le résultat, pop d'échelle puis fondu.
        /// Un headshot prime sur le KO : rouge vif + pop plus fort.</summary>
        public void Notify(bool knocked, bool headshot = false) {
            if (this.group == null) return;
            Color color = headshot ? this.headshotColor : (knocked ? this.knockColor : this.hitColor);
            if (this.strokes != null) {
                foreach (Image stroke in this.strokes) {
                    if (stroke != null) stroke.color = color;
                }
            }
            this.group.alpha = 1f;
            this.group.transform.localScale = Vector3.one * (headshot ? this.headshotPopScale : this.hitPopScale);
            this.visibleUntil = Time.time + this.duration;
        }

        private void Update() {
            if (this.group == null || this.group.alpha <= 0f) return;
            this.group.alpha = Mathf.Clamp01((this.visibleUntil - Time.time) / Mathf.Max(0.05f, this.duration));
            this.group.transform.localScale = Vector3.Lerp(
                this.group.transform.localScale, Vector3.one, Time.deltaTime * 14f);
        }
    }
}
