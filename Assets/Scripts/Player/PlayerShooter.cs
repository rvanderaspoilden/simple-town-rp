using Mirror;
using Sim.Enums;
using Sim.Logging;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Tir TPS v1 avec une arme tenue (item dont la config est <see cref="WeaponConfig"/>).
    ///
    /// Contrôles (owner local, caméra TPS, curseur verrouillé) :
    /// - CLIC DROIT maintenu = visée : le corps s'oriente vers le cap caméra, le bras droit
    ///   est pointé par IK vers le point du crosshair, la tête suit.
    /// - CLIC GAUCHE pendant la visée = tir (cadence de la config).
    ///
    /// Réseau :
    /// - la posture de visée est répliquée via SyncVars (aiming + pitch) — le yaw du corps
    ///   passe déjà par le NetworkTransform ;
    /// - CmdFire(origine, direction) → le serveur valide (arme en main via ServerItemManager,
    ///   cadence, origine plausible), raycast, RENVERSE le joueur touché (knockdown ragdoll,
    ///   pas de dégâts létaux en V1) puis RpcFired(point d'impact) ;
    /// - RpcFired joue sur chaque client les effets (tracer/flash/son/recul) via
    ///   <see cref="WeaponBehaviour.PlayFireEffects"/> + impulsion de recul sur l'IK du bras.
    ///
    /// L'animation de tir est procédurale (IK bras droit + regard + recul) : aucune clip
    /// d'arme requise. Nécessite l'IK Pass activé sur le DERNIER layer du Character.controller
    /// (Left Arm) — un pass sur le Base Layer serait écrasé par les layers de bras (Carry_Mug).
    /// </summary>
    public class PlayerShooter : NetworkBehaviour {
        [Header("Visée (IK bras droit)")]
        [Tooltip("Distance (m) épaule→main en visée (bras tendu ≈ 0.55).")]
        [SerializeField] private float armExtension = 0.55f;
        [Tooltip("Vitesse de fondu du poids IK (entrée/sortie de visée).")]
        [SerializeField] private float ikFadeSpeed = 6f;
        [Tooltip("Trim d'orientation de l'ARME en visée (euler appliqué dans le repère de l'arme, après alignement du canon sur la direction de visée). Zéro = canon exactement sur la visée — le grip de la config est compensé automatiquement, le changer ne désaligne plus la visée.")]
        [SerializeField] private Vector3 handRotationOffset = Vector3.zero;
        [Tooltip("Vitesse (slerp) d'alignement du corps sur le cap caméra pendant la visée.")]
        [SerializeField] private float bodyTurnSpeed = 12f;
        [Tooltip("Clamp du pitch de visée (deg).")]
        [SerializeField] private float pitchClamp = 60f;

        [Header("Recul")]
        [Tooltip("Angle (deg) du kick du canon vers le haut au tir.")]
        [SerializeField] private float recoilAngle = 14f;
        [Tooltip("Recul (m) de la main vers l'épaule au tir.")]
        [SerializeField] private float recoilBack = 0.05f;
        [Tooltip("Vitesse de récupération du recul.")]
        [SerializeField] private float recoilRecoverSpeed = 7f;

        // Posture répliquée : les copies distantes reconstruisent la direction de visée à
        // partir du forward du corps (NetworkTransform) + ce pitch.
        [SyncVar] private bool aiming;
        [SyncVar] private float aimPitch;

        private const float PitchSyncInterval = 0.12f;
        private const float PitchSyncThreshold = 1.5f;
        private const float MaxOriginDistance = 3f;

        private PlayerController player;
        private PlayerHands hands;
        private Animator animator;

        // État local (owner)
        private bool localAiming;
        private Vector3 aimPoint;
        private float nextFireTime;
        private float nextPitchSync;
        private float lastSyncedPitch;

        // État partagé (toutes copies)
        private float ikWeight;
        private float recoil;
        // Côté du bras armé, mémorisé pour que le fondu de sortie reste sur le bon bras.
        private bool ikLeftHand;

        // Serveur
        private double serverNextFireTime;

        private void Awake() {
            this.player = GetComponent<PlayerController>();
            this.hands = GetComponent<PlayerHands>();
            this.animator = GetComponent<Animator>();
        }

        /// <summary>Arme actuellement tenue par CE joueur (copie locale ou distante), sinon null.</summary>
        public WeaponBehaviour HeldWeapon {
            get {
                if (this.hands == null) return null;
                if (this.hands.RightHandItem is WeaponBehaviour right) return right;
                if (this.hands.LeftHandItem is WeaponBehaviour left) return left;
                return null;
            }
        }

        private void Update() {
            if (!isLocalPlayer) return;

            bool wantAim = this.CanAim() && Input.GetMouseButton(1);
            if (wantAim != this.localAiming) {
                this.localAiming = wantAim;
                this.CmdSetAiming(wantAim);
            }

            if (!this.localAiming) return;

            this.UpdateAimPoint();
            this.SyncPitch();

            WeaponBehaviour weapon = this.HeldWeapon;
            if (weapon != null && Input.GetMouseButtonDown(0) && Time.time >= this.nextFireTime) {
                WeaponConfig cfg = weapon.WeaponConfiguration;
                this.nextFireTime = Time.time + (cfg != null ? cfg.FireInterval : 0.35f);

                Vector3 muzzle = weapon.MuzzlePosition;
                Vector3 dir = (this.aimPoint - muzzle).normalized;
                if (dir.sqrMagnitude < 0.5f) dir = this.transform.forward;
                this.CmdFire(muzzle, dir);
            }
        }

        private void LateUpdate() {
            // Pendant la visée, le corps suit le cap caméra (gagne sur la rotation de
            // déplacement posée par TickFreeMove dans Update) → strafe implicite.
            if (!isLocalPlayer || !this.localAiming) return;
            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
            if (cam == null) return;

            Quaternion target = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
            this.transform.rotation = Quaternion.Slerp(this.transform.rotation, target,
                Time.deltaTime * this.bodyTurnSpeed);
        }

        private bool CanAim() {
            if (this.HeldWeapon == null) return false;
            if (this.player == null) return false;
            if (this.player.IsKnockedDown || this.player.IsDriving || this.player.IsPassenger) return false;

            PlayerState state = this.player.PlayerState;
            if (state == PlayerState.DIED || state == PlayerState.SITTING || state == PlayerState.SLEEPING) return false;

            if (CameraManager.Instance == null || CameraManager.Instance.GetMode() != CameraModeEnum.TPS) return false;
            return Cursor.lockState == CursorLockMode.Locked;
        }

        /// <summary>Point visé par le crosshair : premier impact du ray centre-écran qui
        /// n'appartient pas au joueur local, sinon un point à portée max sur le ray.</summary>
        private void UpdateAimPoint() {
            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
            WeaponBehaviour weapon = this.HeldWeapon;
            float range = weapon?.WeaponConfiguration != null ? weapon.WeaponConfiguration.MaxRange : 40f;

            if (cam == null) {
                this.aimPoint = this.transform.position + this.transform.forward * range;
                return;
            }

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            this.aimPoint = ray.GetPoint(range);

            RaycastHit[] hits = Physics.RaycastAll(ray.origin, ray.direction, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits) {
                PlayerController pc = h.collider.GetComponentInParent<PlayerController>();
                if (pc != null && pc.isLocalPlayer) continue;
                this.aimPoint = h.point;
                break;
            }
        }

        private void SyncPitch() {
            WeaponBehaviour weapon = this.HeldWeapon;
            Vector3 shoulder = this.GetShoulderPosition(weapon != null && weapon.HolderHand == HandType.Left);
            Vector3 toTarget = this.aimPoint - shoulder;
            float planar = new Vector2(toTarget.x, toTarget.z).magnitude;
            float pitch = Mathf.Clamp(Mathf.Atan2(toTarget.y, Mathf.Max(0.001f, planar)) * Mathf.Rad2Deg,
                -this.pitchClamp, this.pitchClamp);

            // Lecture locale immédiate ; réplication throttlée aux copies distantes.
            this.aimPitch = pitch;
            if (Time.time >= this.nextPitchSync && Mathf.Abs(pitch - this.lastSyncedPitch) > PitchSyncThreshold) {
                this.nextPitchSync = Time.time + PitchSyncInterval;
                this.lastSyncedPitch = pitch;
                this.CmdSetAimPitch(pitch);
            }
        }

        private Vector3 GetShoulderPosition(bool leftHand) {
            Transform upperArm = this.animator != null
                ? this.animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm)
                : null;
            Vector3 side = (leftHand ? -1f : 1f) * this.transform.right * 0.2f;
            return upperArm != null ? upperArm.position
                : this.transform.position + Vector3.up * 1.4f + side;
        }

        // ── Réseau ────────────────────────────────────────────────────────────────

        [Command]
        private void CmdSetAiming(bool value) {
            this.aiming = value;
        }

        [Command]
        private void CmdSetAimPitch(float value) {
            this.aimPitch = Mathf.Clamp(value, -this.pitchClamp, this.pitchClamp);
        }

        [Command]
        private void CmdFire(Vector3 origin, Vector3 direction) {
            string roomId = PlayerRoomTracker.Instance != null
                ? PlayerRoomTracker.Instance.GetRoom(connectionToClient)
                : null;
            WeaponConfig cfg = ServerItemManager.Instance != null
                ? ServerItemManager.Instance.GetHeldWeaponConfig(netId, roomId)
                : null;
            if (cfg == null) {
                GameLogger.Player.Warning("FireRejected_NoWeapon {PlayerNetId}", netId);
                return;
            }

            // Cadence revalidée serveur (petite tolérance réseau).
            if (NetworkTime.time < this.serverNextFireTime) return;
            this.serverNextFireTime = NetworkTime.time + cfg.FireInterval * 0.9f;

            // Origine plausible : la bouche du canon doit rester proche du corps du tireur.
            if ((origin - this.transform.position).sqrMagnitude > MaxOriginDistance * MaxOriginDistance) {
                GameLogger.Player.Warning("FireRejected_Origin {PlayerNetId}", netId);
                return;
            }
            if (direction.sqrMagnitude < 0.5f) return;
            direction.Normalize();

            Vector3 end = origin + direction * cfg.MaxRange;
            Vector3 hitNormal = -direction;
            bool surfaceHit = false;
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, cfg.MaxRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit h in hits) {
                PlayerController hitPlayer = h.collider.GetComponentInParent<PlayerController>();
                if (hitPlayer == this.player) continue; // sa propre capsule / son arme

                end = h.point;
                hitNormal = h.normal;
                if (hitPlayer != null) {
                    bool knocked = hitPlayer.ServerKnockDown();
                    GameLogger.Player.Info("PlayerShot {ShooterNetId} {TargetNetId} {Knocked}",
                        netId, hitPlayer.netId, knocked);
                } else {
                    // Décor touché (pas un joueur) → décal d'impact côté clients.
                    surfaceHit = true;
                }
                break;
            }

            this.RpcFired(end, hitNormal, surfaceHit);
        }

        [ClientRpc]
        private void RpcFired(Vector3 hitPoint, Vector3 hitNormal, bool surfaceHit) {
            WeaponBehaviour weapon = this.HeldWeapon;
            if (weapon != null) weapon.PlayFireEffects(hitPoint);
            if (surfaceHit) BulletImpactManager.Instance.Spawn(hitPoint, hitNormal);
            this.recoil = 1f;
        }

        // ── Animation procédurale (toutes copies) ─────────────────────────────────

        private void OnAnimatorIK(int layerIndex) {
            // L'IK pass tourne sur le DERNIER layer pour écrire après les layers de bras
            // (sinon Carry_Mug écraserait la pose de visée).
            if (this.animator == null || layerIndex != this.animator.layerCount - 1) return;

            WeaponBehaviour weapon = this.HeldWeapon;

            // L'owner lit son flag local (zéro latence) ; les copies distantes la SyncVar.
            bool wantIk = (isLocalPlayer ? this.localAiming : this.aiming) && weapon != null;
            if (weapon == null) {
                // Arme lâchée : pas de fondu de sortie (la compensation du grip a besoin de l'arme).
                this.ikWeight = 0f;
            } else {
                this.ikLeftHand = weapon.HolderHand == HandType.Left;
                this.ikWeight = Mathf.MoveTowards(this.ikWeight, wantIk ? 1f : 0f, Time.deltaTime * this.ikFadeSpeed);
            }
            if (this.recoil > 0f) {
                this.recoil = Mathf.MoveTowards(this.recoil, 0f, Time.deltaTime * this.recoilRecoverSpeed);
            }

            bool left = this.ikLeftHand;
            AvatarIKGoal goal = left ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
            AvatarIKGoal otherGoal = left ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
            AvatarIKHint hint = left ? AvatarIKHint.LeftElbow : AvatarIKHint.RightElbow;

            // L'autre bras reste entièrement à l'animation.
            this.animator.SetIKPositionWeight(otherGoal, 0f);
            this.animator.SetIKRotationWeight(otherGoal, 0f);

            if (this.ikWeight <= 0.001f) {
                this.animator.SetIKPositionWeight(goal, 0f);
                this.animator.SetIKRotationWeight(goal, 0f);
                this.animator.SetIKHintPositionWeight(hint, 0f);
                this.animator.SetLookAtWeight(0f);
                return;
            }

            Vector3 shoulder = this.GetShoulderPosition(left);
            Vector3 dir = this.GetAimDirection(shoulder);

            // Recul : le canon part vers le haut et la main recule légèrement vers l'épaule.
            Vector3 recoilDir = Quaternion.AngleAxis(-this.recoilAngle * this.recoil, this.transform.right) * dir;
            Vector3 handTarget = shoulder + recoilDir * (this.armExtension - this.recoilBack * this.recoil);

            // Rotation cible de l'ARME : canon (+Z du prefab Glock) sur la direction de visée,
            // plus le trim optionnel. On en déduit la rotation du GOAL IK en compensant la
            // relation goal→arme mesurée en live sur la pose courante (pré-IK). ATTENTION :
            // SetIKRotation pilote le goal du poignet, PAS l'os de la main — chez un avatar
            // humanoïde le goal porte un offset constant (~90° de twist) par rapport à l'os,
            // donc la relation doit être mesurée contre GetIKRotation, jamais contre
            // GetBoneTransform. Le goal et l'arme étant rigidement liés (goal = os × offset
            // constant, os → Handle Position → arme), la relation est constante : le grip de
            // la config peut être réglé librement sans désaligner la visée.
            Quaternion desiredWeapon = Quaternion.LookRotation(recoilDir, Vector3.up)
                                       * Quaternion.Euler(this.handRotationOffset);
            Quaternion goalToWeapon = Quaternion.Inverse(this.animator.GetIKRotation(goal))
                                      * weapon.transform.rotation;
            Quaternion handRotation = desiredWeapon * Quaternion.Inverse(goalToWeapon);

            this.animator.SetIKPositionWeight(goal, this.ikWeight);
            this.animator.SetIKRotationWeight(goal, this.ikWeight);
            this.animator.SetIKPosition(goal, handTarget);
            this.animator.SetIKRotation(goal, handRotation);

            // Coude guidé vers l'extérieur-bas pour une pose de bras naturelle.
            Vector3 outward = (left ? -1f : 1f) * this.transform.right * 0.35f;
            this.animator.SetIKHintPositionWeight(hint, this.ikWeight * 0.6f);
            this.animator.SetIKHintPosition(hint, shoulder + outward - Vector3.up * 0.25f + dir * 0.15f);

            // La tête (et un peu le buste) suivent le point visé.
            this.animator.SetLookAtWeight(this.ikWeight * 0.9f, 0.3f, 0.75f, 0f, 0.5f);
            this.animator.SetLookAtPosition(shoulder + dir * 10f);
        }

        /// <summary>Direction de visée depuis l'épaule : le point crosshair pour l'owner, la
        /// reconstruction forward corps + pitch répliqué pour les copies distantes.</summary>
        private Vector3 GetAimDirection(Vector3 shoulder) {
            if (isLocalPlayer) {
                Vector3 toPoint = this.aimPoint - shoulder;
                if (toPoint.sqrMagnitude > 0.01f) return toPoint.normalized;
            }

            Vector3 flat = this.transform.forward;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
            return Quaternion.AngleAxis(-this.aimPitch, this.transform.right) * flat;
        }
    }
}
