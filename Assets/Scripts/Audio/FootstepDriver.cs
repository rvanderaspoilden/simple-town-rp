using UnityEngine;

namespace Sim.Audio {
    /// <summary>
    /// Joue les bruits de pas d'un personnage, **piloté par la distance parcourue** (un pas tous
    /// les N mètres), pas par un timer ni par des animation events. Avantages : robuste sur un
    /// blend tree de locomotion (pas de double-pas pendant les blends), cadence naturelle à toute
    /// vitesse (la course couvre la distance plus vite → pas plus rapprochés), et fonctionne pour
    /// les joueurs LOCAUX comme DISTANTS sans aucun trafic réseau (la position est déjà répliquée
    /// /interpolée par Mirror — chaque client calcule les pas depuis le transform observé).
    ///
    /// À chaque pas : un court raycast vers le bas détermine la position au sol et, si présent, le
    /// <see cref="FootstepSurface"/> de la surface → choisit le bon <see cref="SfxId"/>. Coût quasi
    /// nul : aucun raycast tant qu'on ne déclenche pas un pas (~2/s en marche).
    /// </summary>
    [DisallowMultipleComponent]
    public class FootstepDriver : MonoBehaviour {
        [Header("Cadence (distance entre deux pas, en mètres)")]
        [SerializeField] private float strideWalk = 1.5f;
        [SerializeField] private float strideRun  = 2.3f;
        [Tooltip("Vitesse (m/s) au-delà de laquelle on considère que le perso court (foulée plus longue).")]
        [SerializeField] private float runSpeed = 3.2f;
        [Tooltip("En dessous de cette vitesse (m/s), le perso est considéré immobile (pas de pas).")]
        [SerializeField] private float minSpeed = 0.4f;

        [Header("Sonde de sol")]
        [SerializeField] private float probeUp = 0.4f;
        [SerializeField] private float probeDown = 1.2f;
        [Tooltip("Couches sondées pour le sol/surface. Par défaut tout sauf Player(8) et NPC(21).")]
        [SerializeField] private LayerMask groundMask = ~((1 << 8) | (1 << 21));

        private Vector3 _lastPos;
        private float   _accum;
        private float   _smoothSpeed;

        private void OnEnable() {
            _lastPos = transform.position;
            _accum = 0f;
            _smoothSpeed = 0f;
        }

        private void Update() {
            Vector3 pos = transform.position;
            Vector3 delta = pos - _lastPos;
            delta.y = 0f;
            _lastPos = pos;

            float dt = Time.deltaTime;
            float speed = dt > 0f ? delta.magnitude / dt : 0f;
            _smoothSpeed = Mathf.Lerp(_smoothSpeed, speed, 0.4f);

            if (_smoothSpeed < minSpeed) { _accum = 0f; return; }

            _accum += delta.magnitude;
            float stride = _smoothSpeed >= runSpeed ? strideRun : strideWalk;
            if (_accum >= stride) {
                _accum -= stride;
                EmitFootstep();
            }
        }

        private void EmitFootstep() {
            Vector3 origin = transform.position + Vector3.up * probeUp;
            Vector3 footPos = transform.position;
            SfxId id = SfxId.FootstepDefault;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeUp + probeDown, groundMask, QueryTriggerInteraction.Ignore)) {
                footPos = hit.point;
                var surf = hit.collider.GetComponentInParent<FootstepSurface>();
                if (surf != null && surf.SurfaceSfx != SfxId.None) id = surf.SurfaceSfx;
            }

            AudioManager.Instance.Play(id, footPos);
        }
    }
}
