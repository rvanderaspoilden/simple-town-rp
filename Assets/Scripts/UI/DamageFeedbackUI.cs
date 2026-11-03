using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Feedback à l'écran du joueur LOCAL quand il encaisse des dégâts d'arme :
    ///   - une VIGNETTE rouge (bords de l'écran) qui flashe à chaque coup puis s'estompe ;
    ///   - pour une BALLE, un indicateur directionnel (chevron autour du centre de l'écran)
    ///     pointant vers l'attaquant, recalé chaque frame sur le cap caméra, fondu ~2 s.
    /// Déclenché par PlayerHealth.TargetDamageTaken (TargetRpc du serveur à la victime).
    /// UI autorée dans le prefab HUD Manager (vignette + template de chevron inactif, cloné).
    /// </summary>
    public class DamageFeedbackUI : MonoBehaviour {
        public static DamageFeedbackUI Instance { get; private set; }

        [Header("Vignette de dégâts")]
        [Tooltip("Image plein écran (sprite radial, alpha sur les bords), teintée rouge par code.")]
        [SerializeField] private Image vignette;
        [Range(0f, 1f)]
        [Tooltip("Alpha de la vignette au moment du coup.")]
        [SerializeField] private float vignetteMaxAlpha = 0.5f;
        [Tooltip("Vitesse de fondu de la vignette (alpha/s).")]
        [SerializeField] private float vignetteFadeSpeed = 0.9f;

        [Header("Indicateur directionnel (balles)")]
        [Tooltip("Template de chevron (enfant INACTIF, ancré au centre), cloné par coup reçu.")]
        [SerializeField] private RectTransform markerTemplate;
        [Tooltip("Rayon (px) du cercle des chevrons autour du centre de l'écran.")]
        [SerializeField] private float markerRadius = 180f;
        [Tooltip("Durée de vie (s) d'un chevron avant fondu complet.")]
        [SerializeField] private float markerDuration = 2f;

        private class Marker {
            public RectTransform Rect;
            public Image Image;
            public Vector3 WorldPos;
            public float DieAt;
        }

        private readonly List<Marker> _markers = new List<Marker>();
        private readonly Queue<RectTransform> _pool = new Queue<RectTransform>();
        private float _vignetteAlpha;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (markerTemplate != null) markerTemplate.gameObject.SetActive(false);
            if (vignette != null) {
                Color c = vignette.color;
                c.a = 0f;
                vignette.color = c;
                vignette.enabled = false;
            }
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        /// <summary>Coup encaissé sans direction connue (mêlée, poings) : vignette seule.</summary>
        public void ShowHit() {
            _vignetteAlpha = Mathf.Max(_vignetteAlpha, vignetteMaxAlpha);
        }

        /// <summary>Balle encaissée : vignette + chevron pointant vers l'attaquant (position
        /// monde au moment du tir, l'angle est recalé chaque frame sur le cap caméra).</summary>
        public void ShowHitFrom(Vector3 attackerWorldPos) {
            ShowHit();
            if (markerTemplate == null) return;

            RectTransform rt = _pool.Count > 0 ? _pool.Dequeue()
                : Instantiate(markerTemplate, markerTemplate.parent);
            rt.gameObject.SetActive(true);
            _markers.Add(new Marker {
                Rect = rt,
                Image = rt.GetComponent<Image>(),
                WorldPos = attackerWorldPos,
                DieAt = Time.time + markerDuration,
            });
        }

        private void Update() {
            TickVignette();
            TickMarkers();
        }

        private void TickVignette() {
            if (vignette == null) return;
            if (_vignetteAlpha <= 0f) {
                if (vignette.enabled) vignette.enabled = false;
                return;
            }
            _vignetteAlpha = Mathf.MoveTowards(_vignetteAlpha, 0f, Time.deltaTime * vignetteFadeSpeed);
            Color c = vignette.color;
            c.a = _vignetteAlpha;
            vignette.color = c;
            vignette.enabled = _vignetteAlpha > 0.001f;
        }

        private void TickMarkers() {
            if (_markers.Count == 0) return;
            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;

            for (int i = _markers.Count - 1; i >= 0; i--) {
                Marker m = _markers[i];
                float remaining = m.DieAt - Time.time;
                if (remaining <= 0f || m.Rect == null || cam == null) { Release(i); continue; }

                // Angle horizontal caméra → attaquant : 0° = devant (haut de l'écran),
                // positif = à droite. Le chevron orbite le centre et pointe vers l'extérieur.
                Vector3 to = m.WorldPos - cam.transform.position;
                to.y = 0f;
                Vector3 fwd = cam.transform.forward;
                fwd.y = 0f;
                if (to.sqrMagnitude < 0.01f || fwd.sqrMagnitude < 0.0001f) { Release(i); continue; }

                float angle = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
                float rad = angle * Mathf.Deg2Rad;
                m.Rect.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * markerRadius;
                m.Rect.localRotation = Quaternion.Euler(0f, 0f, -angle);

                if (m.Image != null) {
                    Color c = m.Image.color;
                    c.a = Mathf.Clamp01(remaining / markerDuration);
                    m.Image.color = c;
                }
            }
        }

        private void Release(int index) {
            Marker m = _markers[index];
            _markers.RemoveAt(index);
            if (m.Rect == null) return;
            m.Rect.gameObject.SetActive(false);
            _pool.Enqueue(m.Rect);
        }
    }
}
