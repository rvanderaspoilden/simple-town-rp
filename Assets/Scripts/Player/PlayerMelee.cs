using System.Collections;
using Mirror;
using Sim.Enums;
using Sim.Logging;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Attaque de mêlée : arme tenue (item dont la config est <see cref="MeleeWeaponConfig"/>,
    /// ex. hache) OU coup de poing à mains nues. Pendant TPS + curseur verrouillé :
    /// - CLIC GAUCHE avec une arme de mêlée = coup (cadence de la config) ;
    /// - CLIC GAUCHE avec les DEUX mains vides = coup de poing (droit d'abord ; en
    ///   enchaînant les clics juste après la fin d'un coup, les poings alternent
    ///   droite/gauche — stats fixes, pas de knockdown).
    /// Pas de visée : le corps s'aligne sur le cap caméra pendant le coup, qui frappe un
    /// cône devant l'attaquant.
    ///
    /// Réseau :
    /// - l'owner joue son swing immédiatement (zéro latence) et envoie CmdAttack ;
    /// - le serveur valide (arme en main via ServerItemManager, cadence), rejoue le swing sur
    ///   les copies distantes (RpcSwing), puis résout le coup À L'INSTANT D'IMPACT
    ///   (StrikeDelay) sur les positions serveur : cible la plus proche dans le cône
    ///   (joueurs + PNJ), ligne de frappe non obstruée par le décor, dégâts PV
    ///   (MeleeWeaponConfig.damage — flinch si non létal, mort ragdoll si fatal), puis
    ///   RpcHit (son d'impact).
    ///
    /// L'animation de coup est procédurale (rotation additive des os du bras droit + torse en
    /// LateUpdate, après l'Animator) : aucun clip requis, identique sur toutes les copies.
    /// Volontairement hors OnAnimatorIK : PlayerShooter y remet les poids IK à zéro quand
    /// aucune arme à feu n'est tenue, les deux systèmes ne se marchent donc jamais dessus.
    /// </summary>
    public class PlayerMelee : NetworkBehaviour {
        // Timeline du swing (s) — partagée par toutes les copies ; l'impact serveur est calé
        // sur la fin de la phase de frappe pour que le coup porte quand la lame descend.
        private const float WindupEnd = 0.2f;
        private const float StrikeEnd = 0.36f;
        private const float SwingDuration = 0.6f;
        private const float StrikeDelay = 0.33f;

        // Timeline du coup de poing (s) — plus courte et plus sèche que le swing d'arme.
        private const float PunchWindupEnd = 0.1f;
        private const float PunchStrikeEnd = 0.2f;
        private const float PunchDuration = 0.38f;
        private const float PunchStrikeDelay = 0.18f;

        // Stats du coup de poing (pas de config : mains nues). Fenêtre de combo = délai
        // après la FIN de l'anim pendant lequel un nouveau clic alterne le poing.
        private const float PunchInterval = 0.45f;
        private const float PunchComboGrace = 0.35f;
        private const float PunchDamage = 5f;
        private const float PunchRange = 1.6f;
        private const float PunchHitAngle = 45f;

        // Tolérance verticale (m) entre attaquant et cible : pas de coup à travers un étage.
        private const float MaxHeightDelta = 2f;
        private const float ChestHeight = 1.2f;

        [Header("Swing procédural (rotation additive des os, LateUpdate)")]
        [Tooltip("Levée du bras (deg) au sommet du windup — rotation du bras autour de l'axe droit du personnage (140 ≈ hache au-dessus de la tête).")]
        [SerializeField] private float raiseAngle = 140f;
        [Tooltip("Angle (deg) restant en fin de frappe (follow-through) avant le retour à la pose animée.")]
        [SerializeField] private float followThroughAngle = 25f;
        [Tooltip("Flexion du coude (deg) au sommet du windup, redressée pendant la frappe.")]
        [SerializeField] private float elbowBendAngle = 35f;
        [Tooltip("Vrille du buste (deg) au sommet du windup (armement du coup), déroulée pendant la frappe.")]
        [SerializeField] private float spineTwistAngle = 14f;
        [Tooltip("Vitesse (slerp) d'alignement du corps sur le cap caméra pendant le swing (owner).")]
        [SerializeField] private float bodyTurnSpeed = 12f;

        [Header("Coup de poing (mains vides)")]
        [Tooltip("Levée du bras (deg) à l'extension du coup — 75 ≈ bras tendu à l'horizontale devant.")]
        [SerializeField] private float punchRaiseAngle = 75f;
        [Tooltip("Flexion du coude (deg) pendant l'armement, dépliée à l'impact.")]
        [SerializeField] private float punchElbowCock = 70f;
        [Tooltip("Vrille du buste (deg) à l'impact — l'épaule du poing part en avant.")]
        [SerializeField] private float punchSpineTwist = 18f;

        private PlayerController player;
        private PlayerHands hands;
        private Animator animator;

        // État local (owner)
        private float nextAttackTime;
        private float lastPunchStartTime = float.NegativeInfinity; // alternance des poings
        private bool lastPunchWasLeft;

        // État partagé (toutes copies) : départ du swing/punch en cours, -∞ = aucun.
        private float swingStartTime = float.NegativeInfinity;
        private bool currentIsPunch;
        private bool punchLeftFist;

        // Serveur
        private double serverNextAttackTime;

        private void Awake() {
            this.player = GetComponent<PlayerController>();
            this.hands = GetComponent<PlayerHands>();
            this.animator = GetComponent<Animator>();
        }

        /// <summary>Un coup (swing d'arme ou punch) est-il en cours sur CETTE copie ?
        /// Consommé par le LateUpdate (pose + alignement caméra de l'owner).</summary>
        public bool IsSwinging
            => Time.time - this.swingStartTime < (this.currentIsPunch ? PunchDuration : SwingDuration);

        /// <summary>Arme de mêlée actuellement tenue par CE joueur (copie locale ou distante), sinon null.</summary>
        public MeleeWeaponBehaviour HeldMeleeWeapon {
            get {
                if (this.hands == null) return null;
                if (this.hands.RightHandItem is MeleeWeaponBehaviour right) return right;
                if (this.hands.LeftHandItem is MeleeWeaponBehaviour left) return left;
                return null;
            }
        }

        /// <summary>Les DEUX mains sont-elles vides sur CETTE copie ? Le sac au dos ne
        /// compte pas (BackItem séparé dans PlayerHands).</summary>
        private bool HandsAreEmpty
            => this.hands != null && this.hands.RightHandItem == null && this.hands.LeftHandItem == null;

        private void Update() {
            if (!isLocalPlayer) return;
            if (!this.CanStrike()) return;
            if (!Input.GetMouseButtonDown(0) || Time.time < this.nextAttackTime) return;

            MeleeWeaponBehaviour weapon = this.HeldMeleeWeapon;
            if (weapon != null) {
                MeleeWeaponConfig cfg = weapon.MeleeConfiguration;
                if (cfg == null) return;
                this.nextAttackTime = Time.time + cfg.AttackInterval;
                this.StartSwing(weapon);
                this.CmdAttack();
            } else if (this.HandsAreEmpty) {
                // Mains nues : coup de poing. Un item tenu SANS être une arme de mêlée
                // (sandwich, liasse…) ne frappe pas — son clic gauche reste l'action rapide.
                bool left = this.NextPunchIsLeft();
                this.nextAttackTime = Time.time + PunchInterval;
                this.StartPunch(left);
                this.CmdPunch(left);
            }
        }

        /// <summary>Alternance des poings : si le clic suit de près la fin du coup précédent
        /// (fenêtre PunchComboGrace), on frappe de l'autre poing ; sinon on repart du droit.</summary>
        private bool NextPunchIsLeft() {
            bool chained = Time.time <= this.lastPunchStartTime + PunchDuration + PunchComboGrace;
            bool left = chained && !this.lastPunchWasLeft;
            this.lastPunchStartTime = Time.time;
            this.lastPunchWasLeft = left;
            return left;
        }

        /// <summary>Mêmes gardes que la visée du PlayerShooter : joueur debout et libre,
        /// caméra TPS, curseur verrouillé (pas d'UI ouverte). L'exigence d'arme ou de mains
        /// vides est tranchée par l'appelant.</summary>
        private bool CanStrike() {
            if (this.player == null) return false;
            if (this.player.IsKnockedDown || this.player.IsDriving || this.player.IsPassenger) return false;

            PlayerState state = this.player.PlayerState;
            if (state == PlayerState.DIED || state == PlayerState.SITTING || state == PlayerState.SLEEPING
                || state == PlayerState.BUSY) return false;

            if (CameraManager.Instance == null || CameraManager.Instance.GetMode() != CameraModeEnum.TPS) return false;
            return Cursor.lockState == CursorLockMode.Locked;
        }

        private void StartSwing(MeleeWeaponBehaviour weapon) {
            this.swingStartTime = Time.time;
            this.currentIsPunch = false;
            if (weapon != null) weapon.PlaySwingEffects();
        }

        private void StartPunch(bool leftFist) {
            this.swingStartTime = Time.time;
            this.currentIsPunch = true;
            this.punchLeftFist = leftFist;
        }

        private void LateUpdate() {
            if (!this.IsSwinging) return;

            // Owner : le corps suit le cap caméra pendant le swing (le coup part devant le
            // regard) — même mécanique que le hip fire du PlayerShooter.
            if (isLocalPlayer) {
                Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
                if (cam != null) {
                    Quaternion target = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
                    this.transform.rotation = Quaternion.Slerp(this.transform.rotation, target,
                        Time.deltaTime * this.bodyTurnSpeed);
                }
            }

            float t = Time.time - this.swingStartTime;
            if (this.currentIsPunch) this.ApplyPunchPose(t);
            else this.ApplySwingPose(t);
        }

        /// <summary>Pose du swing à l'instant t, appliquée PAR-DESSUS la pose animée (LateUpdate
        /// tourne après l'Animator) : levée du bras au-dessus de la tête (windup), descente
        /// rapide (frappe), retour (récupération). Rotation additive en espace monde autour de
        /// l'axe droit du personnage — un angle de levée POSITIF monte le bras vers l'avant-haut.</summary>
        private void ApplySwingPose(float t) {
            if (this.animator == null) return;

            float lift; // 0 = pose animée, raiseAngle = hache au-dessus de la tête
            if (t < WindupEnd) {
                float p = t / WindupEnd;
                lift = this.raiseAngle * (1f - (1f - p) * (1f - p)); // ease-out : armement qui décélère
            } else if (t < StrikeEnd) {
                float p = (t - WindupEnd) / (StrikeEnd - WindupEnd);
                lift = Mathf.Lerp(this.raiseAngle, this.followThroughAngle, p * p); // ease-in : la lame accélère
            } else {
                float p = (t - StrikeEnd) / (SwingDuration - StrikeEnd);
                lift = Mathf.Lerp(this.followThroughAngle, 0f, p);
            }

            Transform upperArm = this.animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform lowerArm = this.animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform chest = this.animator.GetBoneTransform(HumanBodyBones.Chest);

            float raiseRatio = Mathf.Clamp01(lift / Mathf.Max(1f, this.raiseAngle));
            // Rotation NÉGATIVE autour de l'axe droit = bras vers l'avant-haut (le bras pend
            // le long du corps dans la pose animée).
            if (upperArm != null) upperArm.Rotate(this.transform.right, -lift, Space.World);
            if (lowerArm != null) lowerArm.Rotate(this.transform.right, -this.elbowBendAngle * raiseRatio, Space.World);
            if (chest != null) chest.Rotate(Vector3.up, this.spineTwistAngle * raiseRatio, Space.World);
        }

        /// <summary>Pose du coup de poing à l'instant t, même principe additif que le swing
        /// mais sur le bras du poing courant (droit ou gauche) : armement court poing près
        /// du buste (coude plié), extension sèche vers l'avant à l'impact, retour. Le buste
        /// vrille pour envoyer l'épaule du coup en avant.</summary>
        private void ApplyPunchPose(float t) {
            if (this.animator == null) return;
            bool left = this.punchLeftFist;

            float extend; // 0 = pose animée, 1 = bras tendu devant
            float cock;   // 0..1 flexion du coude (armement), dépliée à l'impact
            if (t < PunchWindupEnd) {
                float p = t / PunchWindupEnd;
                extend = 0.55f * p; // le bras monte déjà vers l'avant…
                cock = p;           // …poing armé près du buste
            } else if (t < PunchStrikeEnd) {
                float p = (t - PunchWindupEnd) / (PunchStrikeEnd - PunchWindupEnd);
                extend = Mathf.Lerp(0.55f, 1f, p * p); // le poing accélère vers l'avant
                cock = 1f - p * p;                     // le coude se déplie à l'impact
            } else {
                float p = (t - PunchStrikeEnd) / (PunchDuration - PunchStrikeEnd);
                extend = 1f - p;
                cock = 0f;
            }

            Transform upperArm = this.animator.GetBoneTransform(
                left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lowerArm = this.animator.GetBoneTransform(
                left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform chest = this.animator.GetBoneTransform(HumanBodyBones.Chest);

            if (upperArm != null) upperArm.Rotate(this.transform.right, -this.punchRaiseAngle * extend, Space.World);
            if (lowerArm != null) lowerArm.Rotate(this.transform.right, -this.punchElbowCock * cock, Space.World);
            // Rotation +Y = épaule droite vers l'arrière : à l'impact on veut l'épaule du
            // poing EN AVANT, donc sens négatif pour le poing droit, positif pour le gauche.
            if (chest != null) {
                float twist = this.punchSpineTwist * extend;
                chest.Rotate(Vector3.up, left ? twist : -twist, Space.World);
            }
        }

        // ── Réseau ────────────────────────────────────────────────────────────────

        [Command]
        private void CmdAttack() {
            string roomId = PlayerRoomTracker.Instance != null
                ? PlayerRoomTracker.Instance.GetRoom(connectionToClient)
                : null;
            if (ServerItemManager.Instance == null
                || !ServerItemManager.Instance.TryGetHeldMeleeWeapon(netId, roomId, out _, out MeleeWeaponConfig cfg)) {
                GameLogger.Player.Warning("MeleeRejected_NoWeapon {PlayerNetId}", netId);
                return;
            }

            // Cadence revalidée serveur (petite tolérance réseau).
            if (NetworkTime.time < this.serverNextAttackTime) return;
            this.serverNextAttackTime = NetworkTime.time + cfg.AttackInterval * 0.9f;

            this.RpcSwing();
            this.StartCoroutine(this.ServerResolveStrike(cfg));
        }

        /// <summary>Résolution du coup à l'instant d'impact (StrikeDelay après le départ du
        /// swing) sur l'état serveur : cible la plus proche (joueur ou PNJ) dans le cône
        /// devant l'attaquant, ligne de frappe non obstruée par le décor. Les personnages
        /// sont testés sur leur position serveur (les PNJ serveur n'ont aucun collider) —
        /// le raycast ne sert qu'au décor, comme pour le tir.</summary>
        private IEnumerator ServerResolveStrike(MeleeWeaponConfig cfg) {
            double impactAt = NetworkTime.time + StrikeDelay;
            while (NetworkTime.time < impactAt) yield return null;

            // L'arme doit ENCORE être en main à l'impact (lâchée/échangée pendant le swing = coup blanc).
            string roomId = PlayerRoomTracker.Instance != null
                ? PlayerRoomTracker.Instance.GetRoom(connectionToClient)
                : null;
            if (ServerItemManager.Instance == null
                || !ServerItemManager.Instance.TryGetHeldMeleeWeapon(netId, roomId, out _, out MeleeWeaponConfig heldCfg)
                || heldCfg != cfg) yield break;

            this.ServerResolveImpact(cfg.Damage, cfg.MaxRange, cfg.HitAngle);
        }

        /// <summary>Validation + résolution d'un coup de poing côté serveur : mains vides
        /// (revérifiées à l'impact), cadence fixe, mêmes Rpc de replay que l'arme.</summary>
        [Command]
        private void CmdPunch(bool leftFist) {
            if (ServerItemManager.Instance == null || !ServerItemManager.Instance.PlayerHandsAreEmpty(netId)) {
                GameLogger.Player.Warning("PunchRejected_HandsNotEmpty {PlayerNetId}", netId);
                return;
            }

            if (NetworkTime.time < this.serverNextAttackTime) return;
            this.serverNextAttackTime = NetworkTime.time + PunchInterval * 0.9f;

            this.RpcPunch(leftFist);
            this.StartCoroutine(this.ServerResolvePunch());
        }

        private IEnumerator ServerResolvePunch() {
            double impactAt = NetworkTime.time + PunchStrikeDelay;
            while (NetworkTime.time < impactAt) yield return null;

            // Les mains doivent ENCORE être vides à l'impact (item équipé pendant le coup
            // = coup blanc).
            if (ServerItemManager.Instance == null || !ServerItemManager.Instance.PlayerHandsAreEmpty(netId))
                yield break;

            this.ServerResolveImpact(PunchDamage, PunchRange, PunchHitAngle);
        }

        /// <summary>Résolution commune arme/poing sur l'état serveur à l'instant d'impact :
        /// cible la plus proche (joueur ou PNJ) dans le cône devant l'attaquant, ligne de
        /// frappe non obstruée par le décor, dégâts PV (flinch si non létal, ragdoll de mort
        /// + cadavre fouillable si fatal), RpcHit.</summary>
        [Server]
        private void ServerResolveImpact(float damage, float maxRange, float hitAngle) {
            Vector3 forward = this.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            // Cible la plus proche dans le cône : joueurs (connexions) puis PNJ (registre).
            PlayerController hitPlayer = null;
            NpcAIController hitNpc = null;
            float best = float.MaxValue;
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                PlayerController pc = conn?.identity != null ? conn.identity.GetComponent<PlayerController>() : null;
                if (pc == null || pc == this.player) continue;
                if (pc.IsDead) continue; // cadavre (fouillable) : pas une cible
                if (this.InStrikeCone(maxRange, hitAngle, forward, pc.transform.position, out float d) && d < best) {
                    best = d;
                    hitPlayer = pc;
                }
            }
            foreach (NpcAIController npc in NpcAIController.AllActive) {
                if (npc.IsDead) continue; // cadavre au sol en attente de despawn : pas une cible
                if (this.InStrikeCone(maxRange, hitAngle, forward, npc.transform.position, out float d) && d < best) {
                    best = d;
                    hitNpc = npc;
                    hitPlayer = null;
                }
            }
            if (hitPlayer == null && hitNpc == null) return;

            // Ligne de frappe (poitrine→poitrine) non obstruée par le décor — pas de coup à
            // travers un mur ; les colliders de personnages (variables/absents) sont ignorés.
            Vector3 origin = this.transform.position + Vector3.up * ChestHeight;
            Vector3 targetChest = (hitPlayer != null ? hitPlayer.transform.position : hitNpc.transform.position)
                                  + Vector3.up * ChestHeight;
            if (this.StrikeBlockedByDecor(origin, targetChest)) return;

            // Dégâts PV : coup NON létal = petit choc procédural (flinch), la cible reste
            // debout ; coup FATAL = ragdoll de mort + cadavre fouillable. Plus de knockdown.
            bool killed;
            if (hitPlayer != null) {
                PlayerHealth hitHealth = hitPlayer.GetComponent<PlayerHealth>();
                killed = hitHealth != null && hitHealth.ApplyWeaponDamage(damage);
            } else {
                killed = hitNpc.ServerApplyDamage(damage);
            }
            GameLogger.Player.Info("MeleeHit {AttackerNetId} {Target} {Killed}",
                netId, hitPlayer != null ? "player:" + hitPlayer.netId : "npc:" + hitNpc.OccupantId, killed);

            this.RpcHit(targetChest);
            this.TargetDamageDealt(targetChest, damage, killed);
        }

        /// <summary>Confirmation de coup pour l'ATTAQUANT seul : chiffre de dégâts flottant
        /// au point d'impact + hitmarker sur le réticule. Vérité serveur (envoyé au moment
        /// où les dégâts sont réellement appliqués).</summary>
        [TargetRpc]
        private void TargetDamageDealt(Vector3 hitPoint, float damage, bool killed) {
            DamageNumber.Spawn(hitPoint, damage, killed);
            if (UI.HitmarkerUI.Instance != null) UI.HitmarkerUI.Instance.Notify(killed);
        }

        /// <summary>La cible (pieds à targetPos, position serveur) est-elle frappable : à
        /// portée horizontale, à hauteur comparable et dans le cône devant l'attaquant ?</summary>
        private bool InStrikeCone(float maxRange, float hitAngle, Vector3 forward, Vector3 targetPos, out float dist) {
            Vector3 to = targetPos - this.transform.position;
            float heightDelta = Mathf.Abs(to.y);
            to.y = 0f;
            dist = to.magnitude;
            if (dist > maxRange || heightDelta > MaxHeightDelta) return false;
            return dist < 0.3f || Vector3.Angle(forward, to) <= hitAngle;
        }

        /// <summary>Premier obstacle DÉCOR sur la ligne de frappe (les personnages, aux
        /// colliders variables/absents côté serveur, sont ignorés — ils sont résolus
        /// analytiquement par le cône).</summary>
        private bool StrikeBlockedByDecor(Vector3 origin, Vector3 target) {
            Vector3 to = target - origin;
            float dist = to.magnitude;
            if (dist < 0.05f) return false;

            RaycastHit[] hits = Physics.RaycastAll(origin, to / dist, dist - 0.02f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits) {
                if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (h.collider.GetComponentInParent<ClientNpcView>() != null) continue; // vue PNJ (host)
                if (h.collider.GetComponentInParent<NpcAIController>() != null) continue;
                return true;
            }
            return false;
        }

        [ClientRpc]
        private void RpcSwing() {
            // L'owner a déjà lancé son swing au clic (optimiste) — seules les copies
            // distantes le démarrent ici.
            if (isLocalPlayer) return;
            this.StartSwing(this.HeldMeleeWeapon);
        }

        [ClientRpc]
        private void RpcPunch(bool leftFist) {
            if (isLocalPlayer) return;
            this.StartPunch(leftFist);
        }

        [ClientRpc]
        private void RpcHit(Vector3 hitPoint) {
            MeleeWeaponBehaviour weapon = this.HeldMeleeWeapon;
            if (weapon != null) weapon.PlayHitEffects(hitPoint);
        }
    }
}
