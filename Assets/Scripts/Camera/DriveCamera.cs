using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sim {
    /// <summary>
    /// Caméra de conduite « GTA » (Cinemachine 3.x). Une <see cref="CinemachineCamera"/> avec corps
    /// <see cref="CinemachineOrbitalFollow"/> (Sphere) : on peut REGARDER AUTOUR du véhicule à la
    /// molette enfoncée (comme la caméra libre à pied), et dès qu'on ROULE la caméra se RECENTRE
    /// automatiquement derrière le véhicule — d'autant plus vite que la vitesse est élevée.
    ///
    /// Effets dynamiques selon la vitesse normalisée :
    ///   - FOV : <see cref="fovIdle"/> → <see cref="fovMax"/> (sensation de vitesse).
    ///   - Rayon d'orbite (recul) : <see cref="distIdle"/> → <see cref="distMax"/>.
    ///   - Amplitude du bruit : <see cref="noiseIdle"/> → <see cref="noiseMax"/>.
    ///
    /// L'axe horizontal de l'orbite est piloté ici (la molette ajoute l'angle ; sinon recentrage
    /// vers 0 = derrière le véhicule, à une vitesse qui croît avec celle du véhicule). Le binding
    /// <c>LockToTargetWithWorldUp</c> fait que l'angle 0 est toujours « derrière » le cap.
    /// </summary>
    public class DriveCamera : MonoBehaviour {
        [Header("Références (CM3)")]
        [SerializeField] private CinemachineCamera vcam;
        [SerializeField] private CinemachineOrbitalFollow body;
        [SerializeField] private CinemachineBasicMultiChannelPerlin noise;

        [Header("FOV dynamique")]
        [SerializeField] private float fovIdle = 45f;
        [SerializeField] private float fovMax  = 60f;

        [Header("Recul dynamique (rayon d'orbite)")]
        [SerializeField] private float distIdle = 5f;
        [SerializeField] private float distMax  = 6f;

        [Header("Zoom léger (molette)")]
        [Tooltip("Sensibilité du zoom molette (unités de rayon par cran).")]
        [SerializeField] private float zoomSpeed = 5f;
        [Tooltip("Décalage de rayon min (zoom avant) / max (zoom arrière) ajouté au recul dynamique.")]
        [SerializeField] private float zoomMin = -2f;
        [SerializeField] private float zoomMax = 2f;

        [Header("Bruit dynamique (amplitude)")]
        [SerializeField] private float noiseIdle = 0.1f;
        [SerializeField] private float noiseMax  = 0.35f;

        [Header("Regarder autour / recentrage")]
        [Tooltip("Sensibilité de l'orbite horizontale (degrés par unité de souris), molette enfoncée.")]
        [SerializeField] private float lookSpeed = 15f;
        [Tooltip("Vitesse de recentrage derrière le véhicule à l'ARRÊT (deg/s). 0 = on garde la vue.")]
        [SerializeField] private float recenterAtRest = 0f;
        [Tooltip("Vitesse de recentrage derrière le véhicule à PLEINE vitesse (deg/s).")]
        [SerializeField] private float recenterAtSpeed = 220f;

        [Header("Réactivité des effets")]
        [Tooltip("Vitesse de lerp des effets (FOV / rayon / bruit). Plus grand = plus réactif.")]
        [SerializeField] private float responseLerp = 4f;

        private VehicleController vehicle;
        private float _zoom; // décalage de rayon cumulé par la molette, borné [zoomMin, zoomMax]

        private void OnEnable()  { if (this.vcam != null) this.vcam.gameObject.SetActive(true); }
        private void OnDisable() { if (this.vcam != null) this.vcam.gameObject.SetActive(false); }

        /// <summary>Cible le véhicule (racine) ; null à la sortie.</summary>
        public void SetVehicle(VehicleController target) {
            this.vehicle = target;
            Transform t = target != null ? target.transform : null;
            if (this.vcam != null) { this.vcam.Follow = t; this.vcam.LookAt = t; }
        }

        public CinemachineCamera GetVirtualCamera() => this.vcam;

        private void Update() {
            float s = this.vehicle != null ? Mathf.Clamp01(this.vehicle.NormalizedSpeed) : 0f;
            float dt = Time.deltaTime;
            float k = (this.responseLerp > 0f ? this.responseLerp : 4f) * dt;

            if (this.vcam != null)
                this.vcam.Lens.FieldOfView =
                    Mathf.Lerp(this.vcam.Lens.FieldOfView, Mathf.Lerp(this.fovIdle, this.fovMax, s), k);

            if (this.body != null) {
                bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

                // Zoom léger à la molette (cumulé, borné) ajouté au recul dynamique.
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.0001f && !overUI)
                    this._zoom = Mathf.Clamp(this._zoom - scroll * this.zoomSpeed, this.zoomMin, this.zoomMax);

                // Recul : rayon d'orbite (vitesse) + zoom.
                float targetRadius = Mathf.Lerp(this.distIdle, this.distMax, s) + this._zoom;
                this.body.Radius = Mathf.Lerp(this.body.Radius, targetRadius, k);

                // Regarder autour (molette enfoncée) sinon recentrage derrière le véhicule.
                if (Input.GetMouseButton(2) && !overUI) {
                    this.body.HorizontalAxis.Value += Input.GetAxis("Mouse X") * this.lookSpeed;
                } else {
                    float rec = Mathf.Lerp(this.recenterAtRest, this.recenterAtSpeed, s);
                    this.body.HorizontalAxis.Value =
                        Mathf.MoveTowardsAngle(this.body.HorizontalAxis.Value, 0f, rec * dt);
                }
            }

            if (this.noise != null)
                this.noise.AmplitudeGain =
                    Mathf.Lerp(this.noise.AmplitudeGain, Mathf.Lerp(this.noiseIdle, this.noiseMax, s), k);
        }
    }
}
