using UnityEngine;

namespace Sim.Audio {
    /// <summary>
    /// Joue les bruits de pas EN SYNCHRO avec l'animation, en lisant la hauteur des os de pieds
    /// (gauche/droite) de l'Animator humanoïde. Un pas est émis au MINIMUM LOCAL de hauteur d'un
    /// pied (il descendait, il se met à remonter = il vient de se poser) — détection indépendante
    /// du rig (pas de seuil de hauteur absolu à calibrer). Synchro parfaite avec l'anim visible
    /// quelle qu'elle soit (marche, course, blends, directions), AUCUN animation event à poser,
    /// et fonctionne pour les joueurs locaux comme distants sans trafic réseau.
    ///
    /// À chaque pas : court raycast vers le bas pour la surface (<see cref="FootstepSurface"/>)
    /// → choisit le bon <see cref="SfxId"/>. Garde-fous : vitesse min (pas de pas à l'arrêt) et
    /// anti-rebond par pied.
    /// </summary>
    [DisallowMultipleComponent]
    public class FootstepDriver : MonoBehaviour {
        [Header("Détection de contact (os de pieds)")]
        [Tooltip("Délai min (s) entre deux pas d'un MÊME pied (anti-rebond / filtre le bruit).")]
        [SerializeField] private float footDebounce = 0.15f;
        [Tooltip("Descente minimale (m/frame) avant le minimum pour qu'il compte comme un vrai pas.")]
        [SerializeField] private float descendEpsilon = 0.0004f;
        [Tooltip("Vitesse planaire (m/s) en dessous de laquelle on ne joue pas de pas (perso ~immobile).")]
        [SerializeField] private float minSpeed = 0.35f;
        [Tooltip("Délai min (s) entre DEUX pas QUELCONQUES de ce perso (tous pieds confondus). " +
                 "Tue les doublons aux transitions/blends sans bloquer une foulée normale (>0.3s entre pas).")]
        [SerializeField] private float globalStepDebounce = 0.18f;

        [Header("Sonde de sol")]
        [SerializeField] private float probeUp = 0.4f;
        [SerializeField] private float probeDown = 1.2f;
        [Tooltip("Couches sondées pour la surface. Par défaut tout sauf Player(8) et NPC(21).")]
        [SerializeField] private LayerMask groundMask = ~((1 << 8) | (1 << 21));

        private Animator  _animator;
        private Transform _leftFoot, _rightFoot;
        private float     _leftPrevY, _rightPrevY;
        private float     _leftPrevVy, _rightPrevVy;
        private float     _leftLast, _rightLast;
        private float     _lastStepAny;

        private Vector3 _lastPos;
        private float   _smoothSpeed;

        private void Start() {
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null && _animator.isHuman) {
                _leftFoot  = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            if (_leftFoot == null || _rightFoot == null)
                Debug.LogWarning("[FootstepDriver] Os de pieds introuvables (Animator non humanoïde ?) — pas de bruits de pas.");
            else {
                _leftPrevY = _leftFoot.position.y;
                _rightPrevY = _rightFoot.position.y;
            }
            _lastPos = transform.position;
        }

        private void Update() {
            if (_leftFoot == null || _rightFoot == null) return;

            // Vitesse planaire lissée (gate anti-pas-à-l'arrêt).
            Vector3 delta = transform.position - _lastPos;
            delta.y = 0f;
            _lastPos = transform.position;
            float dt = Time.deltaTime;
            float speed = dt > 0f ? delta.magnitude / dt : 0f;
            _smoothSpeed = Mathf.Lerp(_smoothSpeed, speed, 0.4f);
            bool moving = _smoothSpeed >= minSpeed;

            CheckFoot(_leftFoot,  ref _leftPrevY,  ref _leftPrevVy,  ref _leftLast,  moving);
            CheckFoot(_rightFoot, ref _rightPrevY, ref _rightPrevVy, ref _rightLast, moving);
        }

        private void CheckFoot(Transform foot, ref float prevY, ref float prevVy, ref float lastTime, bool moving) {
            float y = foot.position.y;
            float vy = y - prevY;                 // delta vertical de la frame (∝ vitesse)

            // Minimum local : descendait franchement (prevVy < -eps), repart vers le haut (vy >= 0).
            bool footfall = prevVy < -descendEpsilon && vy >= 0f;
            if (footfall && moving
                && Time.time - lastTime >= footDebounce             // anti-rebond du MÊME pied
                && Time.time - _lastStepAny >= globalStepDebounce) { // anti-doublon tous pieds confondus
                lastTime = Time.time;
                _lastStepAny = Time.time;
                Emit(foot.position);
            }

            prevY = y;
            prevVy = vy;
        }

        private void Emit(Vector3 footWorldPos) {
            Vector3 origin = footWorldPos + Vector3.up * probeUp;
            Vector3 pos = footWorldPos;
            SfxId id = SfxId.FootstepDefault;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeUp + probeDown, groundMask, QueryTriggerInteraction.Ignore)) {
                pos = hit.point;
                id = ResolveSurface(hit.collider);
            }

            AudioManager.Instance.Play(id, pos);
        }

        /// <summary>
        /// Détermine le son de pas de la surface touchée, dans l'ordre :
        /// 1) sol peint → le <see cref="Sim.Scriptables.CoverConfig"/> appliqué (via <see cref="Sim.Building.Ground"/>)
        ///    déclare son SfxId ;
        /// 2) marqueur explicite <see cref="FootstepSurface"/> (props / sols non peints) ;
        /// 3) défaut.
        /// </summary>
        private SfxId ResolveSurface(Collider col) {
            var ground = col.GetComponentInParent<Sim.Building.Ground>();
            if (ground != null) {
                var cover = Sim.DatabaseManager.GetPaintById(ground.CurrentCover.paintConfigId);
                if (cover != null) return cover.FootstepSfx;
            }

            var surf = col.GetComponentInParent<FootstepSurface>();
            if (surf != null && surf.SurfaceSfx != SfxId.None) return surf.SurfaceSfx;

            return SfxId.FootstepDefault;
        }
    }
}
