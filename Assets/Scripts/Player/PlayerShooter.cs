using System.Collections;
using Mirror;
using Sim.Enums;
using Sim.Logging;
using Sim.UI;
using Sim.Utils;
using UnityEngine;
using Key = UnityEngine.InputSystem.Key;

namespace Sim {
    /// <summary>
    /// Tir TPS v1 avec une arme tenue (item dont la config est <see cref="WeaponConfig"/>).
    ///
    /// Contrôles (owner local, caméra TPS, curseur verrouillé) :
    /// - CLIC DROIT maintenu = visée : le corps s'oriente vers le cap caméra, le bras droit
    ///   est pointé par IK vers le point du crosshair, la tête suit.
    /// - CLIC GAUCHE = tir (cadence de la config) ; MAINTENU = rafale automatique si la
    ///   config l'autorise (WeaponConfig.Automatic, ex. AK-47). Viser n'est PAS requis :
    ///   un tir sans visée (hip fire) LÈVE d'abord l'arme en posture de visée, le coup part
    ///   une fois la posture montée, puis l'arme redescend au repos — avec dispersion
    ///   pleine (WeaponConfig.spreadAngle) et le corps tourné vers le cap caméra ; en
    ///   visée la dispersion est fortement réduite (aimSpreadFactor).
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
    ///
    /// Portage à deux mains : une arme TWO_HAND (Foregrip) est tenue à deux mains en
    /// « bas-prêt » au repos (restWeaponPosition/Euler, pose générique commune) ET en appui
    /// pendant la visée ; une arme à UNE main garde son animation normale au repos et ne
    /// reçoit la main de soutien QUE pendant la visée, si l'autre main est libre (sinon
    /// animation à une main inchangée). Fondu continu entre les poses via aimBlend, sur
    /// toutes les copies.
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

        [Header("Visée deux mains (arme TWO_HAND avec enfant Foregrip)")]
        [Tooltip("Trim d'orientation du goal de la main de SOUTIEN (euler, repère de l'arme). Zéro = relation de la main forte mirrorée (comme le grip gauche des items).")]
        [SerializeField] private Vector3 supportHandRotationOffset = Vector3.zero;
        [Range(0f, 1f)]
        [Tooltip("Poids de rotation IK de la main de soutien (0 = garde la rotation animée du poignet, 1 = suit entièrement l'arme).")]
        [SerializeField] private float supportHandRotationWeight = 1f;
        [Tooltip("Point d'appui de la main de soutien pour une arme SANS Foregrip (ex. pistolet tenu à deux mains) : offset local dans le repère de l'arme, la paume en coupe sous/derrière le grip.")]
        [SerializeField] private Vector3 pistolSupportLocalOffset = new Vector3(0f, -0.045f, -0.015f);
        [Tooltip("Distance (m) épaule→main (pistol grip) en visée pour une arme ÉPAULÉE (avec Foregrip) : bras de tir PLIÉ, crosse contre l'épaule. Les armes à une main gardent armExtension (bras tendu).")]
        [SerializeField] private float rifleAimExtension = 0.28f;
        [Range(0f, 1f)]
        [Tooltip("Recentrage de l'arme épaulée vers l'axe du corps (0 = devant l'épaule, 1 = devant l'épaule opposée) — rapproche le Foregrip de la main de soutien.")]
        [SerializeField] private float rifleAimCentering = 0.3f;

        [Header("Portage au repos (arme TWO_HAND, pose IK « bas-prêt »)")]
        [Tooltip("Position de l'ARME (son origine = le pistol grip) dans le repère du personnage, tenue à deux mains sans viser. Mirrorée en X si l'arme est en main gauche. Même pose pour toutes les armes à deux mains.")]
        [SerializeField] private Vector3 restWeaponPosition = new Vector3(0.08f, 1.08f, 0.12f);
        [Tooltip("Orientation de l'ARME (euler, repère du personnage) au repos : canon vers le bas-gauche (low ready).")]
        [SerializeField] private Vector3 restWeaponEuler = new Vector3(40f, -20f, 0f);
        [Tooltip("Vitesse de fondu entre la pose de repos et la pose de visée.")]
        [SerializeField] private float restAimBlendSpeed = 6f;

        [Header("Tir sans visée (hip fire) & dispersion")]
        [Range(0f, 1f)]
        [Tooltip("Facteur appliqué à la dispersion (WeaponConfig.spreadAngle) PENDANT la visée — fortement réduite.")]
        [SerializeField] private float aimSpreadFactor = 0.15f;
        [Tooltip("Durée (s) de la posture de visée autour d'un tir SANS visée : l'arme est levée, le coup part une fois la posture montée, puis l'arme redescend au repos à l'expiration.")]
        [SerializeField] private float hipFirePostureDuration = 0.7f;

        [Header("Munitions & rechargement")]
        [Tooltip("Touche de rechargement : consomme une boîte de munitions de la famille de l'arme (poche puis sac tenu) et remplit le chargeur après WeaponConfig.reloadDuration. Un clic de tir à vide recharge aussi.")]
        [SerializeField] private Key reloadKey = Key.R;

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

        // Capsule analytique des personnages pour la résolution de tir serveur : les PNJ
        // serveur n'ont AUCUN collider (vues client-side) et ceux des joueurs varient selon
        // le mode — le raycast ne sert qu'au décor, les personnages sont testés sur leur
        // position serveur.
        private const float CharacterCapsuleHeight = 1.8f;
        private const float CharacterCapsuleRadius = 0.45f;

        private PlayerController player;
        private PlayerHands hands;
        private Animator animator;

        // État local (owner)
        private bool localAiming;
        private Vector3 aimPoint;
        private float nextFireTime;
        private float nextPitchSync;
        private float lastSyncedPitch;
        // Fenêtre de posture hip fire : jusqu'à cet instant l'arme est levée comme en visée
        // (et le corps de l'owner suit le cap caméra). Prolongée à chaque tir/clic maintenu.
        private float hipPostureUntil;
        // Clic reçu pendant la MONTÉE de l'arme : le tir part dès que la posture est levée.
        private bool hipShotQueued;

        // État partagé (toutes copies)
        private float ikWeight;
        // 0 = pose de repos (bas-prêt deux mains), 1 = pose de visée.
        private float aimBlend = 1f;
        private float recoil;
        // Côté du bras armé, mémorisé pour que le fondu de sortie reste sur le bon bras.
        private bool ikLeftHand;

        // Munitions (owner) : miroir du chargeur serveur (TargetAmmo). -1 = inconnu, en
        // attente de la réponse à CmdQueryAmmo après un changement d'arme — le tir reste
        // permis tant que c'est inconnu, le serveur fait foi.
        private int localLoadedAmmo = -1;
        private float localReloadingUntil;
        private WeaponBehaviour ammoQueriedWeapon;

        // Réserve (owner) : balles supplémentaires portées (boîtes compatibles poche + sac
        // tenu, × un chargeur par boîte), poussée par TargetAmmoReserve. -1 = inconnue.
        private int localAmmoReserve = -1;

        // Serveur
        private double serverNextFireTime;
        private double serverReloadingUntil;
        private bool serverReloadConsuming;

        // Serveur : resynchronisation périodique de la réserve HUD — un sac fermé se lit en
        // DB, on recompte par intervalle (+ resync immédiat au changement d'arme et au reload).
        private const float ReserveSyncInterval = 3f;
        private double serverNextReserveSync;
        private bool serverReserveComputing;
        private int serverLastSentReserve = -1;

        private void Awake() {
            this.player = GetComponent<PlayerController>();
            this.hands = GetComponent<PlayerHands>();
            this.animator = GetComponent<Animator>();
        }

        /// <summary>Visée en cours sur CETTE copie (flag local pour l'owner — zéro latence —,
        /// SyncVar pour les copies distantes). Consommé par la caméra TPS (zoom) et le
        /// déplacement (marche ralentie, course coupée).</summary>
        public bool IsAiming => (isLocalPlayer ? this.localAiming : this.aiming) && this.HeldWeapon != null;

        /// <summary>Owner local : vrai quand le réticule est sur un personnage (joueur distant
        /// ou PNJ) ET que la ligne canon→cible est dégagée — consommé par le point rouge des
        /// réticules (CrosshairUI / ScopeOverlayUI).</summary>
        public bool HasTargetLock { get; private set; }

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
            if (isServer) this.ServerTickReserve();
            if (!isLocalPlayer) return;

            // Changement d'arme (équipée/lâchée/échangée) : le chargeur local devient inconnu,
            // le serveur renvoie son état (TargetAmmo) — l'état vit sur l'ITEM côté serveur.
            WeaponBehaviour heldNow = this.HeldWeapon;
            if (heldNow != this.ammoQueriedWeapon) {
                this.ammoQueriedWeapon = heldNow;
                this.localLoadedAmmo = -1;
                this.localAmmoReserve = -1;
                this.localReloadingUntil = 0f;
                if (heldNow != null) this.CmdQueryAmmo();
            }
            this.UpdateAmmoHud(heldNow);

            bool wantAim = this.CanAim() && Input.GetMouseButton(1);
            if (wantAim != this.localAiming) {
                this.localAiming = wantAim;
                this.CmdSetAiming(wantAim);
            }

            // Point rouge : actif dès que l'état de tir est permis (visée OU hip fire) — le
            // réticule est toujours à l'écran en TPS, le verrou couvre toutes les visées.
            this.UpdateTargetLock(this.CanAim());

            // Rechargement manuel (R) — mêmes gardes que le tir (arme tenue, TPS, curseur verrouillé).
            if (this.CanAim() && GameInput.Down(this.reloadKey)) this.TryStartReload();

            // Tir possible même SANS viser (hip fire) tant que l'état le permet : l'arme est
            // d'abord LEVÉE en posture de visée (fenêtre hipPostureUntil consommée par
            // OnAnimatorIK), le coup part une fois la posture montée, puis l'arme redescend
            // au repos à l'expiration. Dispersion pleine — la visée la réduit fortement.
            if (!this.localAiming && !this.CanAim()) {
                this.hipShotQueued = false;
                return;
            }

            this.UpdateAimPoint();
            if (this.localAiming || Time.time < this.hipPostureUntil) this.SyncPitch();

            WeaponBehaviour weapon = this.HeldWeapon;
            if (weapon == null) return;
            WeaponConfig cfg = weapon.WeaponConfiguration;

            // Arme automatique : le clic MAINTENU enchaîne les tirs à la cadence de la config.
            bool wantsFire = cfg != null && cfg.Automatic
                ? Input.GetMouseButton(0)
                : Input.GetMouseButtonDown(0);

            // Posture montée ? ikWeight et aimBlend progressent dans OnAnimatorIK ; en visée
            // (clic droit) la posture est déjà tenue.
            bool raised = this.localAiming || this.ikWeight * this.aimBlend >= 0.85f;

            if (!this.localAiming) {
                // Ouvre/prolonge la fenêtre de levée tant que le joueur veut tirer ; un clic
                // pendant la montée est mémorisé et part dès que la posture est levée.
                if (wantsFire) this.hipPostureUntil = Time.time + this.hipFirePostureDuration;
                if (!raised && Input.GetMouseButtonDown(0)) this.hipShotQueued = true;
                if (Time.time >= this.hipPostureUntil) this.hipShotQueued = false;
            }

            // Rechargement en cours : aucun tir ne part (le serveur le refuse aussi).
            if (Time.time < this.localReloadingUntil) {
                this.hipShotQueued = false;
                return;
            }

            // Chargeur vide : le CLIC de tir recharge au lieu de tirer (un GetMouseButtonDown
            // sec — pas de spam de reload en maintenant la rafale d'une automatique).
            if (this.localLoadedAmmo == 0) {
                this.hipShotQueued = false;
                if (Input.GetMouseButtonDown(0)) this.TryStartReload();
                return;
            }

            bool fireNow = raised && (wantsFire || this.hipShotQueued);
            if (fireNow && Time.time >= this.nextFireTime) {
                this.hipShotQueued = false;
                // Décompte optimiste (resynchronisé par TargetAmmo à chaque tir serveur).
                if (this.localLoadedAmmo > 0) this.localLoadedAmmo--;
                this.nextFireTime = Time.time + (cfg != null ? cfg.FireInterval : 0.35f);

                Vector3 muzzle = weapon.MuzzlePosition;
                Vector3 dir = (this.aimPoint - muzzle).normalized;
                if (dir.sqrMagnitude < 0.5f) dir = this.transform.forward;

                // Visée à la lunette (sniper) : dispersion NULLE — le tir part exactement sur
                // le réticule ; sinon réduction classique en visée, pleine en hip fire.
                bool scoped = this.localAiming && cfg != null && cfg.ScopedAim;
                float spread = scoped ? 0f
                    : (cfg != null ? cfg.SpreadAngle : 4f) * (this.localAiming ? this.aimSpreadFactor : 1f);
                this.CmdFire(muzzle, ApplySpread(dir, spread));
                // Feedback immédiat du réticule (bloom + flash) — sans attendre le serveur.
                if (CrosshairUI.Instance != null) CrosshairUI.Instance.NotifyShot(cfg != null ? cfg.RecoilKick : 1f);
                if (!this.localAiming) this.hipPostureUntil = Time.time + this.hipFirePostureDuration;
            }
        }

        private void LateUpdate() {
            // Pendant la visée, le corps suit le cap caméra (gagne sur la rotation de
            // déplacement posée par TickFreeMove dans Update) → strafe implicite.
            bool facingCamera = this.localAiming || Time.time < this.hipPostureUntil;
            if (!isLocalPlayer || !facingCamera) return;
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
            if (state == PlayerState.DIED || state == PlayerState.SITTING || state == PlayerState.SLEEPING
                || state == PlayerState.BUSY) return false;

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

        // ── Verrou de cible (point rouge) ──────────────────────────────────────────

        /// <summary>Recalcule le verrou de cible de l'owner et le pousse aux deux réticules
        /// (crosshair TPS et overlay lunette — un seul est visible à la fois).</summary>
        private void UpdateTargetLock(bool active) {
            this.HasTargetLock = active && this.ComputeTargetLock();
            if (CrosshairUI.Instance != null) CrosshairUI.Instance.SetTargetLock(this.HasTargetLock);
            if (ScopeOverlayUI.Instance != null) ScopeOverlayUI.Instance.SetTargetLock(this.HasTargetLock);
        }

        /// <summary>Vrai si le réticule centre-écran est sur un personnage (joueur distant ou
        /// PNJ) ET que rien n'obstrue la ligne CANON→point visé — la caméra épaule voit
        /// par-dessus des obstacles (murets, capots...) que le canon ne franchit pas.</summary>
        private bool ComputeTargetLock() {
            WeaponBehaviour weapon = this.HeldWeapon;
            Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
            if (weapon == null || cam == null) return false;
            float range = weapon.WeaponConfiguration != null ? weapon.WeaponConfiguration.MaxRange : 40f;

            // 1) Un personnage sous le réticule ? (vues client : copies distantes de joueurs
            //    + ClientNpcView — le premier impact décide, un décor devant = pas de cible.)
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Component targetRoot = null;
            Vector3 targetPoint = Vector3.zero;
            RaycastHit[] hits = Physics.RaycastAll(ray.origin, ray.direction, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits) {
                PlayerController pc = h.collider.GetComponentInParent<PlayerController>();
                if (pc != null && pc.isLocalPlayer) continue; // sa propre capsule / son arme

                if (pc != null) {
                    targetRoot = pc;
                    targetPoint = h.point;
                } else {
                    ClientNpcView npc = h.collider.GetComponentInParent<ClientNpcView>();
                    if (npc != null) {
                        targetRoot = npc;
                        targetPoint = h.point;
                    }
                }
                break;
            }
            if (targetRoot == null) return false;

            // 2) Ligne canon→point visé dégagée ? Le premier impact non-local doit appartenir
            //    à la cible elle-même ; rien sur la ligne = dégagé aussi.
            Vector3 muzzle = weapon.MuzzlePosition;
            Vector3 toTarget = targetPoint - muzzle;
            float dist = toTarget.magnitude;
            if (dist < 0.05f) return true;

            RaycastHit[] line = Physics.RaycastAll(muzzle, toTarget / dist, dist - 0.02f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(line, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in line) {
                PlayerController pc = h.collider.GetComponentInParent<PlayerController>();
                if (pc != null && pc.isLocalPlayer) continue;
                if (pc != null && (Component)pc == targetRoot) return true;

                ClientNpcView npc = h.collider.GetComponentInParent<ClientNpcView>();
                if (npc != null && (Component)npc == targetRoot) return true;
                return false; // obstacle avant la cible
            }
            return true;
        }

        // ── Munitions & rechargement (owner) ──────────────────────────────────────

        /// <summary>Chargeur de l'arme tenue tel que connu par l'owner local (-1 = inconnu).
        /// Consommé par la tooltip d'inventaire du slot main.</summary>
        public int LocalLoadedAmmo => this.localLoadedAmmo;

        /// <summary>Vrai si l'option « Recharger » doit être proposée pour cette arme tenue :
        /// chargeur pas plein, pas de rechargement en cours, et une boîte compatible en stock
        /// (réserve encore inconnue = proposé, le serveur tranchera).</summary>
        public bool CanOfferReload(WeaponBehaviour weapon) {
            if (!isLocalPlayer || weapon == null || weapon != this.HeldWeapon) return false;
            WeaponConfig cfg = weapon.WeaponConfiguration;
            if (cfg == null) return false;
            if (Time.time < this.localReloadingUntil) return false;
            if (this.localLoadedAmmo >= 0 && this.localLoadedAmmo >= cfg.MagazineSize) return false;
            return this.localAmmoReserve != 0;
        }

        /// <summary>Rechargement demandé depuis l'UI (action « Recharger » du menu de l'arme)
        /// — mêmes gardes que la touche R.</summary>
        public void RequestReload() {
            if (isLocalPlayer) this.TryStartReload();
        }

        /// <summary>Demande un rechargement au serveur : consomme une boîte de munitions de la
        /// famille de l'arme (poche puis sac tenu), chargeur rempli après ReloadDuration. Le
        /// blocage local du tir est optimiste — annulé par TargetReloadResult en cas d'échec.</summary>
        private void TryStartReload() {
            WeaponBehaviour weapon = this.HeldWeapon;
            WeaponConfig cfg = weapon != null ? weapon.WeaponConfiguration : null;
            if (cfg == null) return;
            if (Time.time < this.localReloadingUntil) return;
            if (this.localLoadedAmmo >= 0 && this.localLoadedAmmo >= cfg.MagazineSize) return; // déjà plein

            this.localReloadingUntil = Time.time + cfg.ReloadDuration;
            this.CmdReload();
        }

        /// <summary>Pousse l'état du chargeur aux deux réticules (crosshair TPS et overlay
        /// lunette — un seul est visible), ainsi que le MODE du réticule : arme à distance en
        /// main = réticule d'arme (crochets), sinon réticule par défaut. Sans arme en main,
        /// le compteur disparaît.</summary>
        private void UpdateAmmoHud(WeaponBehaviour weapon) {
            WeaponConfig cfg = weapon != null ? weapon.WeaponConfiguration : null;
            if (CrosshairUI.Instance != null) CrosshairUI.Instance.SetWeaponState(cfg != null, this.localAiming);
            if (cfg == null) {
                if (CrosshairUI.Instance != null) CrosshairUI.Instance.ClearAmmo();
                if (ScopeOverlayUI.Instance != null) ScopeOverlayUI.Instance.ClearAmmo();
                return;
            }

            bool reloading = Time.time < this.localReloadingUntil;
            if (CrosshairUI.Instance != null)
                CrosshairUI.Instance.SetAmmo(this.localLoadedAmmo, cfg.MagazineSize, this.localAmmoReserve, reloading);
            if (ScopeOverlayUI.Instance != null)
                ScopeOverlayUI.Instance.SetAmmo(this.localLoadedAmmo, cfg.MagazineSize, this.localAmmoReserve, reloading);
        }

        /// <summary>Serveur : recompte périodiquement la réserve de munitions portée (poche +
        /// sac tenu) de CE joueur et la pousse à l'owner quand elle change. Le recompte est
        /// async (sac fermé lu en DB) — jamais plus d'un en vol.</summary>
        private void ServerTickReserve() {
            if (this.serverReserveComputing || NetworkTime.time < this.serverNextReserveSync) return;
            if (connectionToClient == null || ServerItemManager.Instance == null) return;
            this.serverNextReserveSync = NetworkTime.time + ReserveSyncInterval;

            string roomId = PlayerRoomTracker.Instance != null
                ? PlayerRoomTracker.Instance.GetRoom(connectionToClient)
                : null;
            if (!ServerItemManager.Instance.TryGetHeldWeapon(netId, roomId, out _, out WeaponConfig cfg)) return;

            this.serverReserveComputing = true;
            ServerItemManager.Instance.CountAmmoReserve(connectionToClient, cfg, rounds => {
                if (this == null) return; // joueur déconnecté pendant la lecture DB
                this.serverReserveComputing = false;
                if (rounds == this.serverLastSentReserve) return;
                this.serverLastSentReserve = rounds;
                this.TargetAmmoReserve(rounds);
            });
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

        // Allonge réelle des bras (épaule→coude→main), mesurée une fois sur les os — les
        // longueurs d'os sont constantes, seule la pose change.
        private float leftArmLength;
        private float rightArmLength;

        private float GetArmLength(bool leftArm) {
            float cached = leftArm ? this.leftArmLength : this.rightArmLength;
            if (cached > 0f) return cached;

            Transform upper = this.animator.GetBoneTransform(leftArm ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lower = this.animator.GetBoneTransform(leftArm ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = this.animator.GetBoneTransform(leftArm ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            float length = upper != null && lower != null && hand != null
                ? Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position)
                : 0.55f;

            if (leftArm) this.leftArmLength = length; else this.rightArmLength = length;
            return length;
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

        /// <summary>Owner → serveur : le chargeur de l'arme tenue a changé de main locale —
        /// renvoie son état (l'état vit sur l'ITEM serveur, il suit l'arme au drop/échange).</summary>
        [Command]
        private void CmdQueryAmmo() {
            string roomId = PlayerRoomTracker.Instance != null
                ? PlayerRoomTracker.Instance.GetRoom(connectionToClient)
                : null;
            if (ServerItemManager.Instance == null
                || !ServerItemManager.Instance.TryGetHeldWeapon(netId, roomId, out int weaponEntityId, out WeaponConfig cfg)) return;
            this.TargetAmmo(ServerItemManager.Instance.GetWeaponLoadedAmmo(weaponEntityId, cfg));

            // Nouvelle arme en main : l'owner a remis sa réserve à « inconnue » — recompte
            // immédiat et renvoi forcé (même valeur comprise).
            this.serverLastSentReserve = -1;
            this.serverNextReserveSync = 0;
        }

        [Command]
        private void CmdReload() {
            if (this.serverReloadConsuming || NetworkTime.time < this.serverReloadingUntil) return;

            string roomId = PlayerRoomTracker.Instance != null
                ? PlayerRoomTracker.Instance.GetRoom(connectionToClient)
                : null;
            if (ServerItemManager.Instance == null
                || !ServerItemManager.Instance.TryGetHeldWeapon(netId, roomId, out int weaponEntityId, out WeaponConfig cfg)) {
                this.TargetReloadResult(false, null);
                return;
            }
            if (ServerItemManager.Instance.GetWeaponLoadedAmmo(weaponEntityId, cfg) >= cfg.MagazineSize) {
                this.TargetReloadResult(false, null); // déjà plein — silencieux, juste déverrouiller le client
                return;
            }

            // Tir bloqué pendant TOUT le rechargement, consommation DB comprise ; la boîte
            // est consommée d'abord (pas d'annulation), le chargeur se remplit à l'échéance.
            this.serverReloadConsuming = true;
            this.serverReloadingUntil = NetworkTime.time + cfg.ReloadDuration;
            double reloadEnd = this.serverReloadingUntil;
            ServerItemManager.Instance.ConsumeAmmoItem(connectionToClient, cfg.AmmoCategory, (ok, error) => {
                if (this == null) return; // joueur déconnecté pendant l'aller-retour backend
                this.serverReloadConsuming = false;
                this.serverNextReserveSync = 0; // réserve possiblement changée (boîte consommée ou stock désynchronisé)
                if (!ok) {
                    this.serverReloadingUntil = 0;
                    this.TargetReloadResult(false, error);
                    return;
                }
                this.StartCoroutine(this.ServerFinishReload(reloadEnd, cfg.AmmoCategory));
            });
        }

        /// <summary>Fin de rechargement serveur : à l'échéance, remplit le chargeur de l'arme
        /// ENCORE tenue si elle consomme la même famille de munitions (arme lâchée/changée
        /// entre-temps → la boîte consommée est perdue, cas limite accepté).</summary>
        private IEnumerator ServerFinishReload(double reloadEnd, AmmoCategory category) {
            while (NetworkTime.time < reloadEnd) yield return null;

            string roomId = PlayerRoomTracker.Instance != null
                ? PlayerRoomTracker.Instance.GetRoom(connectionToClient)
                : null;
            if (ServerItemManager.Instance != null
                && ServerItemManager.Instance.TryGetHeldWeapon(netId, roomId, out int weaponEntityId, out WeaponConfig cfg)
                && cfg.AmmoCategory == category) {
                ServerItemManager.Instance.SetWeaponLoadedAmmo(weaponEntityId, cfg.MagazineSize);
                this.TargetAmmo(cfg.MagazineSize);
                GameLogger.Player.Info("WeaponReloaded {PlayerNetId} {Category} {Magazine}", netId, category, cfg.MagazineSize);
            }
            this.TargetReloadResult(true, null);
        }

        [TargetRpc]
        private void TargetAmmo(int loaded) {
            this.localLoadedAmmo = loaded;
        }

        [TargetRpc]
        private void TargetAmmoReserve(int rounds) {
            this.localAmmoReserve = rounds;
        }

        [TargetRpc]
        private void TargetReloadResult(bool success, string error) {
            if (success) return;
            this.localReloadingUntil = 0f; // déverrouille le tir local
            if (!string.IsNullOrEmpty(error)) WorldToastManager.ShowError(error);
        }

        [Command]
        private void CmdFire(Vector3 origin, Vector3 direction) {
            string roomId = PlayerRoomTracker.Instance != null
                ? PlayerRoomTracker.Instance.GetRoom(connectionToClient)
                : null;
            if (ServerItemManager.Instance == null
                || !ServerItemManager.Instance.TryGetHeldWeapon(netId, roomId, out int weaponEntityId, out WeaponConfig cfg)) {
                GameLogger.Player.Warning("FireRejected_NoWeapon {PlayerNetId}", netId);
                return;
            }

            // Rechargement en cours : aucun tir.
            if (this.serverReloadConsuming || NetworkTime.time < this.serverReloadingUntil) return;

            // Cadence revalidée serveur (petite tolérance réseau).
            if (NetworkTime.time < this.serverNextFireTime) return;
            this.serverNextFireTime = NetworkTime.time + cfg.FireInterval * 0.9f;

            // Chargeur : une balle par tir, vide = refus (le client gate déjà — resync au cas où).
            int loadedAmmo = ServerItemManager.Instance.GetWeaponLoadedAmmo(weaponEntityId, cfg);
            if (loadedAmmo <= 0) {
                this.TargetAmmo(0);
                return;
            }
            ServerItemManager.Instance.SetWeaponLoadedAmmo(weaponEntityId, loadedAmmo - 1);
            this.TargetAmmo(loadedAmmo - 1);

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

            // 1) Occlusion DÉCOR : premier obstacle qui n'est ni le tireur ni un personnage.
            //    Les personnages sont résolus ANALYTIQUEMENT (étape 2) : les PNJ serveur n'ont
            //    AUCUN collider (les vues collisionnées sont client-side, le rayon les
            //    traversait — « tirer sur un PNJ ne fait rien ») et les colliders des joueurs
            //    varient selon le mode — le raycast ne fait donc foi que pour le décor.
            float decorDist = cfg.MaxRange;
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, cfg.MaxRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits) {
                if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (h.collider.GetComponentInParent<ClientNpcView>() != null) continue; // vue PNJ (host)
                if (h.collider.GetComponentInParent<NpcAIController>() != null) continue;
                decorDist = h.distance;
                end = h.point;
                hitNormal = h.normal;
                surfaceHit = true;
                break;
            }

            // 2) PERSONNAGES : capsule analytique sur les positions serveur — la plus proche
            //    touchée AVANT le décor gagne (joueurs via les connexions, PNJ via le registre).
            PlayerController hitPlayer = null;
            NpcAIController hitNpc = null;
            float best = decorDist;
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                PlayerController pc = conn?.identity != null ? conn.identity.GetComponent<PlayerController>() : null;
                if (pc == null || pc == this.player) continue;
                if (pc.IsDead) continue; // cadavre (fouillable) : pas une cible
                if (RayHitsCapsule(origin, direction, best, pc.transform.position, out float d) && d < best) {
                    best = d;
                    hitPlayer = pc;
                }
            }
            foreach (NpcAIController npc in NpcAIController.AllActive) {
                if (npc.IsDead) continue; // cadavre au sol en attente de despawn : pas une cible
                if (RayHitsCapsule(origin, direction, best, npc.transform.position, out float d) && d < best) {
                    best = d;
                    hitNpc = npc;
                    hitPlayer = null;
                }
            }

            if (hitPlayer != null || hitNpc != null) {
                end = origin + direction * best;
                hitNormal = -direction;
                surfaceHit = false; // pas de décal d'impact sur un personnage
                // Dégâts PV : joueur comme PNJ perdent des points de vie. Coup NON létal =
                // petit choc procédural (flinch), la cible reste debout ; coup FATAL =
                // ragdoll de mort + cadavre fouillable. Plus aucun knockdown à l'impact.
                bool killed;
                if (hitPlayer != null) {
                    PlayerHealth hitHealth = hitPlayer.GetComponent<PlayerHealth>();
                    // Position du TIREUR transmise : la victime affiche la direction du coup.
                    killed = hitHealth != null && hitHealth.ApplyWeaponDamage(cfg.Damage, this.transform.position);
                } else {
                    // L'attaquant est transmis : un PNJ HOSTILE touché riposte (aggro).
                    killed = hitNpc.ServerApplyDamage(cfg.Damage, this.player);
                }
                GameLogger.Player.Info("CharacterShot {ShooterNetId} {Target} {Killed}",
                    netId, hitPlayer != null ? "player:" + hitPlayer.netId : "npc:" + hitNpc.OccupantId, killed);
                this.TargetDamageDealt(end, cfg.Damage, killed);
            }

            this.RpcFired(end, hitNormal, surfaceHit);
        }

        /// <summary>Confirmation de coup pour le TIREUR seul : chiffre de dégâts flottant au
        /// point d'impact + hitmarker sur le réticule (orange = coup fatal). Vérité serveur
        /// (envoyé au moment où les dégâts sont réellement appliqués) — même feedback que la
        /// mêlée/les poings.</summary>
        [TargetRpc]
        private void TargetDamageDealt(Vector3 hitPoint, float damage, bool killed) {
            DamageNumber.Spawn(hitPoint, damage, killed);
            if (HitmarkerUI.Instance != null) HitmarkerUI.Instance.Notify(killed);
        }

        [ClientRpc]
        private void RpcFired(Vector3 hitPoint, Vector3 hitNormal, bool surfaceHit) {
            WeaponBehaviour weapon = this.HeldWeapon;
            if (weapon != null) weapon.PlayFireEffects(hitPoint);
            if (surfaceHit) BulletImpactManager.Instance.Spawn(hitPoint, hitNormal);

            // Copies distantes : un tir non visé lève brièvement l'arme en posture de visée
            // (l'owner a déjà ouvert sa propre fenêtre AVANT le tir).
            if (!(isLocalPlayer ? this.localAiming : this.aiming)) {
                this.hipPostureUntil = Mathf.Max(this.hipPostureUntil, Time.time + this.hipFirePostureDuration);
            }

            // Kick par tir pondéré par la config (les automatiques accumulent en rafale,
            // plafonné à 1 pour que le canon ne grimpe pas indéfiniment).
            float kick = weapon?.WeaponConfiguration != null ? weapon.WeaponConfiguration.RecoilKick : 1f;
            this.recoil = Mathf.Min(1f, this.recoil + kick);
        }

        // ── Animation procédurale (toutes copies) ─────────────────────────────────

        private void OnAnimatorIK(int layerIndex) {
            // L'IK pass tourne sur le DERNIER layer pour écrire après les layers de bras
            // (sinon Carry_Mug écraserait la pose de visée).
            if (this.animator == null || layerIndex != this.animator.layerCount - 1) return;

            WeaponBehaviour weapon = this.HeldWeapon;

            // L'owner lit son flag local (zéro latence) ; les copies distantes la SyncVar.
            // Pendant la fenêtre hip fire, l'arme est traitée comme EN VISÉE : levée vers le
            // crosshair avant le tir, redescente au repos à l'expiration.
            bool hipPosture = weapon != null && Time.time < this.hipPostureUntil;
            bool aimingNow = ((isLocalPlayer ? this.localAiming : this.aiming) || hipPosture) && weapon != null;

            // Main de soutien : le Foregrip pour une arme TWO_HAND ; pour une arme à UNE main,
            // la seconde main vient en appui sous le grip SI elle est libre — et UNIQUEMENT
            // en visée (au repos une arme 1H garde son animation à une main actuelle).
            Transform foregrip = weapon != null && weapon.Configuration != null
                                 && weapon.Configuration.HandleType == ItemHandleType.TWO_HAND
                ? weapon.Foregrip : null;
            bool mainHandLeft = weapon != null && weapon.HolderHand == HandType.Left;
            bool otherHandFree = weapon != null && this.hands != null
                && (mainHandLeft ? this.hands.RightHandItem == null : this.hands.LeftHandItem == null);
            bool hasSupport = foregrip != null || otherHandFree;

            // Portage au repos : RÉSERVÉ aux armes TWO_HAND (Foregrip) — une arme à une
            // main garde son animation normale hors visée. Pose générique
            // (restWeaponPosition/Euler), identique sur toutes les copies.
            bool restHold = !aimingNow && foregrip != null && this.CanHoldPose(weapon);
            bool wantIk = aimingNow || restHold;

            if (weapon == null) {
                // Arme lâchée : pas de fondu de sortie (la compensation du grip a besoin de l'arme).
                this.ikWeight = 0f;
            } else {
                this.ikLeftHand = weapon.HolderHand == HandType.Left;
                this.ikWeight = Mathf.MoveTowards(this.ikWeight, wantIk ? 1f : 0f, Time.deltaTime * this.ikFadeSpeed);
            }
            // Fondu pose repos ↔ pose visée. Sans pose de repos (pas de Foregrip), on reste
            // côté visée pendant les fondus d'entrée/sortie d'IK.
            this.aimBlend = Mathf.MoveTowards(this.aimBlend,
                aimingNow || foregrip == null ? 1f : 0f, Time.deltaTime * this.restAimBlendSpeed);
            if (this.recoil > 0f) {
                this.recoil = Mathf.MoveTowards(this.recoil, 0f, Time.deltaTime * this.recoilRecoverSpeed);
            }

            bool left = this.ikLeftHand;
            AvatarIKGoal goal = left ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
            AvatarIKGoal otherGoal = left ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
            AvatarIKHint hint = left ? AvatarIKHint.LeftElbow : AvatarIKHint.RightElbow;
            AvatarIKHint otherHint = left ? AvatarIKHint.RightElbow : AvatarIKHint.LeftElbow;
            this.animator.SetIKPositionWeight(otherGoal, 0f);
            this.animator.SetIKRotationWeight(otherGoal, 0f);
            this.animator.SetIKHintPositionWeight(otherHint, 0f);

            if (this.ikWeight <= 0.001f) {
                this.animator.SetIKPositionWeight(goal, 0f);
                this.animator.SetIKRotationWeight(goal, 0f);
                this.animator.SetIKHintPositionWeight(hint, 0f);
                this.animator.SetLookAtWeight(0f);
                return;
            }

            Vector3 shoulder = this.GetShoulderPosition(left);
            // Arme épaulée (Foregrip) : l'arme est ancrée sur un point ramené vers l'axe du
            // corps. La direction de visée part de cette ANCRE — pas de l'épaule — pour que le
            // canon CONVERGE exactement sur le point du crosshair (depuis l'épaule, la ligne de
            // tir resterait décalée en parallèle de la visée).
            Vector3 aimAnchor = foregrip != null
                ? Vector3.Lerp(shoulder, this.GetShoulderPosition(!left), this.rifleAimCentering)
                : shoulder;
            Vector3 dir = this.GetAimDirection(aimAnchor);

            // Relation rigide goal→arme mesurée en live sur la pose courante (pré-IK).
            // ATTENTION : SetIKRotation pilote le goal du poignet, PAS l'os de la main — chez
            // un avatar humanoïde le goal porte un offset constant (~90° de twist) par rapport
            // à l'os, donc la relation doit être mesurée contre GetIKRotation, jamais contre
            // GetBoneTransform. Le goal et l'arme étant rigidement liés (goal = os × offset
            // constant, os → Handle Position → arme), la relation est constante : le grip de
            // la config peut être réglé librement sans désaligner la visée.
            Quaternion goalRotNow = this.animator.GetIKRotation(goal);
            Quaternion goalToWeapon = Quaternion.Inverse(goalRotNow) * weapon.transform.rotation;
            Vector3 goalToWeaponPos = Quaternion.Inverse(goalRotNow)
                                      * (weapon.transform.position - this.animator.GetIKPosition(goal));

            // Pose cible de l'ARME en VISÉE : canon (+Z du prefab) sur la direction de visée
            // (+ trim), recul (kick du canon vers le haut, main vers l'épaule), bras tendu.
            Vector3 recoilDir = Quaternion.AngleAxis(-this.recoilAngle * this.recoil, this.transform.right) * dir;
            Quaternion aimWeaponRot = Quaternion.LookRotation(recoilDir, Vector3.up)
                                      * Quaternion.Euler(this.handRotationOffset);
            // Arme épaulée (Foregrip) : bras de tir plié (crosse à l'épaule) ; arme 1H : bras
            // tendu devant l'épaule, comme avant.
            float extension = foregrip != null ? this.rifleAimExtension : this.armExtension;
            Vector3 aimHandPos = aimAnchor + recoilDir * (extension - this.recoilBack * this.recoil);
            Vector3 aimWeaponPos = aimHandPos + (aimWeaponRot * Quaternion.Inverse(goalToWeapon)) * goalToWeaponPos;
            // La ligne de CANON ne passe pas par l'origine de l'arme (le grip) : sur l'AK le
            // canon court 10 cm AU-DESSUS. Sans correction, l'axe du canon reste décalé en
            // parallèle de la ligne de visée et ne pointe jamais le crosshair. On décale
            // l'arme de l'offset latéral du Muzzle pour que le CANON soit sur la ligne de visée.
            Vector3 muzzleLateral = weapon.transform.InverseTransformPoint(weapon.MuzzlePosition);
            muzzleLateral.z = 0f;
            aimWeaponPos -= aimWeaponRot * muzzleLateral;

            // Pose cible de l'ARME au REPOS (bas-prêt), dans le repère du personnage ;
            // mirrorée si l'arme est tenue à gauche.
            Vector3 restLocalPos = this.restWeaponPosition;
            Vector3 restEuler = this.restWeaponEuler;
            if (left) {
                restLocalPos.x = -restLocalPos.x;
                restEuler = new Vector3(restEuler.x, -restEuler.y, -restEuler.z);
            }
            Quaternion restWeaponRot = this.transform.rotation * Quaternion.Euler(restEuler);
            Vector3 restWeaponPos = this.transform.TransformPoint(restLocalPos);

            // Pose finale de l'arme = fondu repos→visée ; le goal principal s'en déduit en
            // compensant la relation goal→arme. Pendant OnAnimatorIK, weapon.transform est
            // encore à la pose animée : tout ce qui dépend de la pose VOULUE de l'arme (main
            // de soutien incluse) se calcule à partir de weaponPos/weaponRot, jamais du transform.
            Quaternion weaponRot = Quaternion.Slerp(restWeaponRot, aimWeaponRot, this.aimBlend);
            Vector3 weaponPos = Vector3.Lerp(restWeaponPos, aimWeaponPos, this.aimBlend);
            Quaternion handRotation = weaponRot * Quaternion.Inverse(goalToWeapon);
            Vector3 handTarget = weaponPos - handRotation * goalToWeaponPos;

            this.animator.SetIKPositionWeight(goal, this.ikWeight);
            this.animator.SetIKRotationWeight(goal, this.ikWeight);
            this.animator.SetIKPosition(goal, handTarget);
            this.animator.SetIKRotation(goal, handRotation);

            // Main de soutien : sur le Foregrip (arme TWO_HAND) ou en appui sous le grip
            // (arme 1 main, l'autre main libre) — point exprimé dans la pose cible de l'arme ;
            // rotation = relation goal→arme de la main forte MIRRORÉE (même convention
            // (x,-y,-z,w) que le grip gauche des items) + trim.
            if (hasSupport) {
                Vector3 supportLocal = foregrip != null
                    ? weapon.transform.InverseTransformPoint(foregrip.position)
                    : this.pistolSupportLocalOffset;
                Vector3 supportPos = weaponPos + weaponRot * supportLocal;

                // Allonge du bras de soutien : si le point d'appui est hors de portée, la main
                // GLISSE vers la crosse LE LONG de l'arme (elle reste posée dessus) au lieu de
                // flotter en deçà du Foregrip avec le bras tendu dans le vide.
                Vector3 supportShoulder = this.GetShoulderPosition(!left);
                float reach = this.GetArmLength(!left) * 0.95f;
                Vector3 rel = supportPos - supportShoulder;
                if (rel.sqrMagnitude > reach * reach) {
                    Vector3 axis = weaponRot * Vector3.forward;
                    float along = Vector3.Dot(rel, axis);
                    Vector3 perp = rel - axis * along;
                    float perpSq = perp.sqrMagnitude;
                    supportPos = along > 0f && perpSq < reach * reach
                        ? supportShoulder + perp + axis * Mathf.Sqrt(reach * reach - perpSq)
                        : supportShoulder + rel.normalized * reach;
                }

                Quaternion relGoal = Quaternion.Inverse(goalToWeapon);
                Quaternion mirroredRelGoal = new Quaternion(relGoal.x, -relGoal.y, -relGoal.z, relGoal.w);
                Quaternion supportRot = weaponRot
                                        * Quaternion.Euler(this.supportHandRotationOffset)
                                        * mirroredRelGoal;

                this.animator.SetIKPositionWeight(otherGoal, this.ikWeight);
                this.animator.SetIKRotationWeight(otherGoal, this.ikWeight * this.supportHandRotationWeight);
                this.animator.SetIKPosition(otherGoal, supportPos);
                this.animator.SetIKRotation(otherGoal, supportRot);

                // Coude de soutien guidé vers l'extérieur-bas de SON côté.
                Vector3 supportOutward = (left ? 1f : -1f) * this.transform.right * 0.3f;
                this.animator.SetIKHintPositionWeight(otherHint, this.ikWeight * 0.6f);
                this.animator.SetIKHintPosition(otherHint,
                    (supportShoulder + supportPos) * 0.5f + supportOutward - Vector3.up * 0.2f);
            }

            // Coude principal : extérieur-bas (vers l'avant en visée, replié au repos).
            Vector3 outward = (left ? -1f : 1f) * this.transform.right * 0.35f;
            Vector3 aimHint = shoulder + outward - Vector3.up * 0.25f + dir * 0.15f;
            Vector3 restHint = shoulder + outward - Vector3.up * 0.35f - this.transform.forward * 0.1f;
            this.animator.SetIKHintPositionWeight(hint, this.ikWeight * 0.6f);
            this.animator.SetIKHintPosition(hint, Vector3.Lerp(restHint, aimHint, this.aimBlend));

            // La tête (et un peu le buste) suivent le point visé — seulement en visée, le
            // portage au repos laisse le regard à l'animation.
            this.animator.SetLookAtWeight(this.ikWeight * this.aimBlend * 0.9f, 0.3f, 0.75f, 0f, 0.5f);
            this.animator.SetLookAtPosition(shoulder + dir * 10f);
        }

        /// <summary>Le portage au repos à deux mains est-il permis dans l'état courant du
        /// porteur ? Coupé quand l'item est caché par une action (SIT, SLEEP…), au sol (KO),
        /// en véhicule ou mort — la visée a ses propres gardes dans CanAim.</summary>
        private bool CanHoldPose(WeaponBehaviour weapon) {
            if (weapon == null || !weapon.RenderersVisible) return false;
            if (this.player == null) return false;
            if (this.player.IsKnockedDown || this.player.IsDriving || this.player.IsPassenger) return false;
            PlayerState state = this.player.PlayerState;
            return state != PlayerState.DIED && state != PlayerState.SITTING && state != PlayerState.SLEEPING;
        }

        /// <summary>Intersection approchée rayon ↔ capsule verticale de personnage (pieds à
        /// basePos, hauteur/rayon des constantes de classe) : vrai si le rayon passe à moins
        /// du rayon de l'axe de la capsule, avec l'abscisse du point d'approche dans dist.
        /// Paire de points la plus proche entre le rayon (origin + t·dir, t ∈ [0, maxDist],
        /// dir normalisé) et l'axe (p0 + s·seg, s ∈ [0, 1]) — formule standard (Ericson).</summary>
        private static bool RayHitsCapsule(Vector3 origin, Vector3 dir, float maxDist, Vector3 basePos, out float dist) {
            Vector3 p0 = basePos + Vector3.up * CharacterCapsuleRadius;
            Vector3 seg = Vector3.up * Mathf.Max(0.01f, CharacterCapsuleHeight - 2f * CharacterCapsuleRadius);

            Vector3 r = origin - p0;
            float b = Vector3.Dot(dir, seg);
            float c = Vector3.Dot(seg, seg);
            float d = Vector3.Dot(dir, r);
            float e = Vector3.Dot(seg, r);
            float denom = c - b * b; // a = dir·dir = 1

            float t = denom > 1e-5f ? (b * e - c * d) / denom : -d; // parallèle : projection de p0
            t = Mathf.Clamp(t, 0f, maxDist);
            float s = Mathf.Clamp01((b * t + e) / c);
            t = Mathf.Clamp(Vector3.Dot(p0 + seg * s - origin, dir), 0f, maxDist);

            Vector3 onRay = origin + dir * t;
            Vector3 onAxis = p0 + seg * s;
            dist = t;
            return (onRay - onAxis).sqrMagnitude <= CharacterCapsuleRadius * CharacterCapsuleRadius;
        }

        /// <summary>Déviation aléatoire dans un cône de demi-angle donné (deg) autour de dir.</summary>
        private static Vector3 ApplySpread(Vector3 dir, float angleDeg) {
            if (angleDeg <= 0f) return dir;
            Vector3 deviated = dir + Random.insideUnitSphere * Mathf.Tan(angleDeg * Mathf.Deg2Rad);
            return deviated.sqrMagnitude > 0.0001f ? deviated.normalized : dir;
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
