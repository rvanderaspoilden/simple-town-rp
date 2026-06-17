using System.Collections.Generic;
using Interaction;
using Mirror;
using Sim;
using Sim.Audio;
using Sim.Enums;
using UnityEngine;
using UnityEngine.AI;
using Action = Sim.Interactables.Action;

/// <summary>
/// Véhicule conduisible (POC). NetworkBehaviour autonome avec sa propre NetworkIdentity
/// et un NetworkTransformReliable en AUTORITÉ CLIENT (syncDirection ClientToServer) —
/// même modèle que le joueur : le conducteur calcule le mouvement en local et le diffuse.
///
/// Namespace GLOBAL volontairement (cf. CLAUDE.md : la réflexion Mirror n'aime pas les
/// namespaces mixtes sur le wire pour les types réseau).
///
/// Flux entrée/sortie :
///   1. Le joueur survole le véhicule → action "Monter" (IInteractable) → CmdEnterVehicle.
///   2. Serveur : si libre, AssignClientAuthority(conducteur) + driverNetId = sa netId,
///      et PARENTE le joueur sous le siège (côté serveur, pour que le relais NT soit correct).
///   3. Le hook driverNetId parente aussi le joueur sur tous les clients (ride-along visuel)
///      et, pour le conducteur LOCAL, déclenche l'état CharacterDrive (caméra + pose).
///   4. Le conducteur (isOwned) conduit au clavier ; le NT diffuse la position.
///   5. Touche X → CmdExitVehicle : RemoveClientAuthority + driverNetId = 0 + dé-parentage.
///
/// PARENTAGE & RÉSEAU : NetworkTransform synchronise la position LOCALE. Pour que le serveur
/// relaie une position monde cohérente, le parentage est appliqué partout (serveur + clients)
/// via <see cref="ApplyParenting"/>.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class VehicleController : NetworkBehaviour, IInteractable {

    [Header("Anchors")]
    [Tooltip("Place du conducteur : le joueur y est parenté (localPos/localRot remis à zéro).")]
    [SerializeField] private Transform seatAnchor;
    [Tooltip("Cible suivie par la caméra pendant la conduite.")]
    [SerializeField] private Transform cameraAnchor;
    [Tooltip("Point de descente à la sortie (échantillonné sur le NavMesh).")]
    [SerializeField] private Transform exitAnchor;
    [Tooltip("Sièges passagers (hors conducteur). Le nombre utilisable est borné par " +
             "config.passengerCount-1 et par MaxPassengerSlots.")]
    [SerializeField] private Transform[] passengerSeats = new Transform[0];

    [Header("Configuration")]
    [Tooltip("SO décrivant ce véhicule (modèle, capacités, conduite, sons). Source de vérité ; " +
             "si absent, on retombe sur les valeurs par défaut ci-dessous.")]
    [SerializeField] private VehicleConfig config;

    [Header("Physique (WheelCollider)")]
    [Tooltip("Les 4 WheelColliders, dans l'ordre : avant-gauche, avant-droit, arrière-gauche, arrière-droit. " +
             "Les 2 premiers (avant) braquent ; les 4 reçoivent couple moteur et frein.")]
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[0];
    [Tooltip("Couple moteur max par roue (N·m).")]
    [SerializeField] private float motorTorque = 1200f;
    [Tooltip("Angle de braquage max des roues avant (deg).")]
    [SerializeField] private float maxSteerAngleDeg = 30f;
    [Tooltip("Couple de frein (Espace, ou inversion de sens) par roue (N·m).")]
    [SerializeField] private float brakeTorque = 2500f;
    [Tooltip("Frein moteur en roue libre (accélérateur relâché) par roue (N·m).")]
    [SerializeField] private float engineBrakeTorque = 250f;
    [Tooltip("Facteur de vitesse max en marche arrière (× vitesse max avant).")]
    [Range(0.1f, 1f)] [SerializeField] private float reverseSpeedFactor = 0.4f;
    [Tooltip("Abaissement du centre de masse (m, local) pour la stabilité anti-tonneau.")]
    [SerializeField] private float centerOfMassDrop = 0.5f;
    [Tooltip("Lissage du braquage (vitesse d'interpolation de l'angle des roues avant). " +
             "Avec maxSteerAngleDeg, contrôle la FORCE/réactivité de rotation.")]
    [SerializeField] private float steerLerp = 6f;

    [Header("Adhérence / dérapage")]
    [Tooltip("Adhérence latérale des roues AVANT (rigidité de friction). Plus haut = tourne plus net.")]
    [SerializeField] private float frontGrip = 2f;
    [Tooltip("Adhérence latérale des roues ARRIÈRE. La BAISSER fait déraper l'arrière (drift permanent).")]
    [SerializeField] private float rearGrip = 2f;
    [Tooltip("Adhérence latérale arrière quand le frein à main (Espace) est tenu : basse = drift à la demande.")]
    [SerializeField] private float handbrakeRearGrip = 0.6f;

    [Header("Vitesse")]
    [Tooltip("Vitesse max de repli (m/s) si aucune VehicleConfig n'est assignée (sinon config.maxSpeed). " +
             "Sert de plafond au couple moteur.")]
    [SerializeField] private float maxSpeed = 12f;

    [Header("Engine audio")]
    [Tooltip("Pitch de la boucle moteur au ralenti et à pleine vitesse.")]
    [SerializeField] private float enginePitchIdle = 0.8f;
    [SerializeField] private float enginePitchMax  = 1.7f;

    [Header("Health / collisions")]
    [Tooltip("Particules émises en continu quand le véhicule est KO (fumée).")]
    [SerializeField] private ParticleSystem koParticles;
    [Tooltip("Vitesse de choc (m/s) en-dessous de laquelle un impact n'inflige aucun dégât.")]
    [SerializeField] private float minImpactSpeed = 2f;
    [Tooltip("Dégâts infligés par (m/s) au-dessus du seuil.")]
    [SerializeField] private float impactDamageFactor = 6f;

    [Header("Détection / interaction")]
    [Tooltip("Demi-extents de la boîte de détection des personnages percutés (renversement).")]
    [SerializeField] private Vector3 sweepHalfExtents = new Vector3(0.9f, 0.5f, 1.6f);
    [SerializeField] private float interactionRange = 3f;

    [Header("Renversement des personnages (ragdoll)")]
    [Tooltip("Vitesse mini (m/s) pour renverser un personnage percuté. En-dessous, on le traverse sans effet.")]
    [SerializeField] private float minKnockdownSpeed = 2f;
    [Tooltip("Force d'impulsion du ragdoll par m/s de vitesse à l'impact.")]
    [SerializeField] private float knockImpulsePerSpeed = 12f;
    [Tooltip("Délai mini (s) avant de pouvoir re-renverser le MÊME personnage.")]
    [SerializeField] private float characterHitCooldown = 1f;

    [SyncVar(hook = nameof(OnDriverChanged))]
    private uint driverNetId;

    [SyncVar(hook = nameof(OnHealthChanged))]
    private float health = -1f; // -1 = non initialisé (le serveur le règle à MaxHealth au spawn)

    [SyncVar(hook = nameof(OnLockedChanged))]
    private bool isLocked;

    [SyncVar(hook = nameof(OnFuelChanged))]
    private float fuel = -1f; // -1 = non initialisé (le serveur le règle à MaxFuel au spawn)

    // Feux : bits packés dans un octet répliqué (un seul SyncVar → trafic minimal).
    private const byte LIGHT_HEAD  = 1; // phares avant
    private const byte LIGHT_BRAKE = 2; // feux stop
    [SyncVar(hook = nameof(OnLightFlagsChanged))]
    private byte lightFlags;

    // Propriétaire PERSISTANT (character id), hydraté depuis la DB au spawn. "" = non possédé.
    [SyncVar]
    private string ownerCharacterId = "";

    // Id DB du véhicule (non vide = véhicule sorti du GARAGE). Sert à l'identifier pour le rangement
    // et à savoir s'il est déjà dehors. Vide pour les véhicules de scène / d'exposition.
    [SyncVar]
    private string vehicleDbId = "";

    // Clé stable + modèle, côté serveur uniquement (assignés par le spawner) pour la persistance.
    private string vehicleKey;
    private bool   _ownershipInitialized;
    private bool   _stateInitialized; // true = vie/essence restaurées du garage → OnStartServer ne réinitialise pas

    // Sièges passagers : un SyncVar par place (le projet n'utilise pas de collections sync).
    private const int MaxPassengerSlots = 3;
    [SyncVar(hook = nameof(OnPassenger0Changed))] private uint passenger0NetId;
    [SyncVar(hook = nameof(OnPassenger1Changed))] private uint passenger1NetId;
    [SyncVar(hook = nameof(OnPassenger2Changed))] private uint passenger2NetId;

    private Action _enterAction;
    private Action _exitAction;
    private Action _lockAction;
    private Action _unlockAction;
    private Action _refuelAction;
    private Action _openTrunkAction;
    private Action _repairAction;
    private float  _currentSpeed;
    private VehicleWheels _wheels;      // animation visuelle des roues (braquage piloté par l'input du conducteur)
    private VehicleLights _lights;      // visuel des feux (piloté par lightFlags via le hook)
    private byte   _desiredFlags;       // état des feux voulu par le conducteur (edge-triggered → Cmd)
    private Vector3 _velocity;          // vitesse PLANAIRE monde (≠ cap → permet le dérapage latéral)
    private float  _fuelAccum;          // carburant consommé localement, envoyé au serveur par paquets
    private float  _fuelSendTimer;      // cadence d'envoi de la consommation
    private VehicleFuelBar _fuelBar;    // jauge monde « Réservoir » affichée au ravitaillement
    private const float FuelBarHeight = 2.4f; // hauteur d'affichage au-dessus du véhicule (m)
    private AudioSource _engineSource;
    private AudioSource _brakeSource;   // boucle de freinage, coupée dès que le frein est relâché
    private int    _characterMask;   // Player | NPC : cible du renversement (séparé du masque mur)
    private int    _impactMask;      // layers infligeant des dégâts d'impact (murs/décor, pas sol/persos)
    private readonly Collider[] _hitBuffer = new Collider[8];
    private readonly Dictionary<uint, float> _playerHitTime = new Dictionary<uint, float>();
    private readonly Dictionary<int, float>  _npcHitTime    = new Dictionary<int, float>();
    private Rigidbody _rb;             // corps physique (non-kinematic chez le simulateur d'autorité)
    private float     _steerAngleCurrent; // angle de braquage lissé courant
    private float     _lastCollisionDamageTime = -10f;

    public Transform CameraAnchor => cameraAnchor;
    public Transform ExitAnchor   => exitAnchor;
    public bool      IsOccupied   => driverNetId != 0;
    public VehicleConfig Config   => config;
    public string    OwnerCharacterId => ownerCharacterId;
    /// <summary>Id DB si ce véhicule a été sorti d'un garage ("" sinon). Identifie un véhicule possédé sorti.</summary>
    public string    VehicleDbId  => vehicleDbId;

    // ── Vitesse max effective (config si présente, sinon repli) ──────────────────────
    private float MaxSpeed => config != null ? config.maxSpeed : maxSpeed;

    /// <summary>Vitesse planaire courante (m/s), lue sur le Rigidbody. Owner-only fiable (le corps y
    /// simule) ; les copies distantes sont kinematic → ~0 (acceptable, le HUD vitesse est conducteur-only).</summary>
    private float CurrentSpeedMs => _rb != null ? Vector3.ProjectOnPlane(_rb.linearVelocity, transform.up).magnitude : 0f;

    /// <summary>Vitesse normalisée [0..1] (vitesse / vitesse max). Pilote l'effet de vitesse caméra + pitch moteur.</summary>
    public float NormalizedSpeed => MaxSpeed > 0f ? Mathf.Clamp01(CurrentSpeedMs / MaxSpeed) : 0f;

    /// <summary>Vitesse courante en km/h. Affichée par le HUD conducteur.</summary>
    public float SpeedKmh => CurrentSpeedMs * 3.6f;

    /// <summary>Vrai là où CETTE instance doit simuler la physique : le client propriétaire (conducteur)
    /// OU le serveur quand le véhicule n'a pas de propriétaire client (garé). Ailleurs → kinematic
    /// (le NetworkTransform impose la position).</summary>
    private bool ShouldSimulate => isOwned || (isServer && netIdentity.connectionToClient == null);

    /// <summary>Active/désactive la simulation physique selon l'autorité. À appeler à chaque changement
    /// d'autorité (entrée/sortie conducteur, spawn).</summary>
    private void RefreshSimulationState() {
        if (_rb == null) return;
        bool sim = ShouldSimulate;
        _rb.isKinematic = !sim;
        if (sim) _rb.WakeUp();
    }

    // ── Santé ────────────────────────────────────────────────────────────────────────
    public float MaxHealth => config != null ? config.maxHealth : 100f;
    /// <summary>Vie [0..1] pour la barre / le HUD. (-1 = pas encore synchronisé → plein.)</summary>
    public float HealthNormalized => health < 0f ? 1f : (MaxHealth > 0f ? Mathf.Clamp01(health / MaxHealth) : 0f);
    /// <summary>Véhicule hors d'usage : ne roule plus, fume.</summary>
    public bool IsKO => health == 0f;

    // ── Carburant ──────────────────────────────────────────────────────────────────
    public float MaxFuel => config != null ? config.fuelCapacity : 50f;
    /// <summary>Coût de réparation (configuré sur le SO ; repli 500 si pas de config).</summary>
    public int RepairCost => config != null ? config.repairCost : 500;
    /// <summary>Niveau de carburant [0..1] pour la jauge HUD. (-1 = pas encore synchronisé → plein.)</summary>
    public float FuelNormalized => fuel < 0f ? 1f : (MaxFuel > 0f ? Mathf.Clamp01(fuel / MaxFuel) : 0f);
    /// <summary>Reste-t-il du carburant ? (-1 = non initialisé → considéré plein.)</summary>
    public bool HasFuel => fuel != 0f;

    public override void OnStartServer() {
        // Son de coffre : le système de conteneurs signale l'ouverture/fermeture (pas de prop à animer).
        ServerItemManager.OnVehicleTrunkStateChanged += OnTrunkStateChanged;
        RefreshSimulationState();
        // Ne pas écraser l'état restauré du garage (ServerInitFromGarage tourne AVANT le spawn).
        if (_stateInitialized) return;
        health = MaxHealth;
        fuel = MaxFuel;
    }

    /// <summary>Point UNIQUE de sauvegarde : tout despawn serveur (rangement, déconnexion du
    /// conducteur — Mirror détruit ses objets à autorité —, arrêt) persiste vie + essence.
    /// Le coffre (place DB séparée « container:{id} ») persiste indépendamment.</summary>
    public override void OnStopServer() {
        ServerItemManager.OnVehicleTrunkStateChanged -= OnTrunkStateChanged;
        if (!string.IsNullOrEmpty(vehicleDbId) && ApiManager.Instance != null)
            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.UpdateVehicleStateCoroutine(vehicleDbId, ServerHealth, ServerFuel));
    }

    /// <summary>Coffre ouvert/fermé pour CE véhicule (filtré par uuid) → son 3D pour tous.</summary>
    [Server]
    private void OnTrunkStateChanged(string vehicleUuid, bool open) {
        if (!string.IsNullOrEmpty(vehicleUuid) && vehicleUuid == ServerTrunkUuid())
            RpcTrunkSound(open);
    }

    [ClientRpc]
    private void RpcTrunkSound(bool open) {
        AudioClip clip = config != null ? (open ? config.trunkOpen : config.trunkClose) : null;
        if (clip != null) AudioManager.Instance.PlayClip3D(clip, transform.position);
    }

    /// <summary>Vie/essence courantes côté serveur (pour la persistance au rangement).</summary>
    public float ServerHealth => health < 0f ? MaxHealth : health;
    public float ServerFuel   => fuel   < 0f ? MaxFuel   : fuel;

    /// <summary>Appelé quand le CONDUCTEUR (détenteur de l'autorité) se déconnecte : on lui retire
    /// l'autorité et on libère le véhicule pour que Mirror ne le DÉTRUISE pas (il détruit les objets
    /// à autorité de la connexion qui part). Le véhicule reste donc garé dans le monde.</summary>
    [Server]
    public void ServerHandleOwnerDisconnect() {
        if (driverNetId != 0) ServerReleaseDriver();
    }

    // ── Sièges passagers ─────────────────────────────────────────────────────────────
    /// <summary>Nombre de places passagers exploitables : min(slots SyncVar, ancres, capacité config-1).</summary>
    private int PassengerCapacity {
        get {
            int byConfig = (config != null ? config.passengerCount : 1) - 1;
            int byAnchors = passengerSeats != null ? passengerSeats.Length : 0;
            return Mathf.Clamp(Mathf.Min(byConfig, byAnchors), 0, MaxPassengerSlots);
        }
    }

    private uint GetPassenger(int i) => i == 0 ? passenger0NetId : i == 1 ? passenger1NetId : passenger2NetId;

    [Server]
    private void SetPassenger(int i, uint v) {
        if (i == 0) passenger0NetId = v;
        else if (i == 1) passenger1NetId = v;
        else passenger2NetId = v;
    }

    private bool IsOccupant(uint id) {
        if (id == 0) return false;
        if (driverNetId == id) return true;
        for (int i = 0; i < MaxPassengerSlots; i++) if (GetPassenger(i) == id) return true;
        return false;
    }

    private bool HasFreeSeat() {
        if (driverNetId == 0) return true;
        for (int i = 0; i < PassengerCapacity; i++) if (GetPassenger(i) == 0) return true;
        return false;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake() {
        Action proto = Resources.Load<Action>("Configurations/Actions/ENTER_VEHICLE");
        if (proto != null) {
            _enterAction = Instantiate(proto);
            _enterAction.OnExecute += OnEnterActionExecuted;
        }
        Action exitProto = Resources.Load<Action>("Configurations/Actions/EXIT_VEHICLE");
        if (exitProto != null) {
            _exitAction = Instantiate(exitProto);
            _exitAction.OnExecute += OnExitActionExecuted;
        }

        // Lock / unlock / coffre : configs DÉDIÉES au véhicule (icône + libellé personnalisables
        // sans impacter les actions génériques LOCK/UNLOCK/OPEN des portes & conteneurs).
        Action lockProto = Resources.Load<Action>("Configurations/Actions/VEHICLE_LOCK");
        if (lockProto != null) {
            _lockAction = Instantiate(lockProto);
            _lockAction.OnExecute += OnLockActionExecuted;
        }
        Action unlockProto = Resources.Load<Action>("Configurations/Actions/VEHICLE_UNLOCK");
        if (unlockProto != null) {
            _unlockAction = Instantiate(unlockProto);
            _unlockAction.OnExecute += OnUnlockActionExecuted;
        }
        Action refuelProto = Resources.Load<Action>("Configurations/Actions/REFUEL");
        if (refuelProto != null) {
            _refuelAction = Instantiate(refuelProto);
            _refuelAction.OnExecute += OnRefuelActionExecuted;
        }
        Action trunkProto = Resources.Load<Action>("Configurations/Actions/VEHICLE_TRUNK");
        if (trunkProto != null) {
            _openTrunkAction = Instantiate(trunkProto);
            _openTrunkAction.OnExecute += OnOpenTrunkActionExecuted;
        }
        Action repairProto = Resources.Load<Action>("Configurations/Actions/REPAIR");
        if (repairProto != null) {
            _repairAction = Instantiate(repairProto);
            _repairAction.OnExecute += OnRepairActionExecuted;
        }

        _wheels = GetComponent<VehicleWheels>();
        _lights = GetComponent<VehicleLights>();

        SetupEngineAudio();
        SetKoParticles(false);

        // Corps physique : centre de masse abaissé (anti-tonneau) + interpolation pour un rendu fluide.
        _rb = GetComponent<Rigidbody>();
        if (_rb != null) {
            _rb.centerOfMass += Vector3.down * centerOfMassDrop;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.linearDamping = 0.05f; // léger ralentissement en roue libre → s'arrête naturellement
        }

        // Masque des personnages renversables (Player + NPC).
        int playerLayer = LayerMask.NameToLayer("Player");
        int npcLayer    = LayerMask.NameToLayer("NPC");
        _characterMask  = (playerLayer >= 0 ? 1 << playerLayer : 0) | (npcLayer >= 0 ? 1 << npcLayer : 0);

        // La voiture (corps physique) doit TRAVERSER les personnages, pas les heurter : on désactive la
        // collision physique entre son layer et Player/NPC. Sans effet sur les persos (déplacés en
        // NavMesh, hors physique). Le renversement reste détecté par OverlapBox (qui ignore la matrice).
        int selfLayer = gameObject.layer;
        if (playerLayer >= 0) Physics.IgnoreLayerCollision(selfLayer, playerLayer, true);
        if (npcLayer    >= 0) Physics.IgnoreLayerCollision(selfLayer, npcLayer, true);

        // Dégâts d'impact : tout SAUF sol, personnages (renversement) et items.
        int softLayers = _characterMask;
        foreach (string n in new[] { "Ground", "Ragdoll", "Item" }) {
            int l = LayerMask.NameToLayer(n);
            if (l >= 0) softLayers |= 1 << l;
        }
        _impactMask = ~softLayers;

        if (maxSteerAngleDeg <= 0f) maxSteerAngleDeg = 30f;
    }

    public override void OnStartClient() {
        // Applique l'état de vie initial (la barre + les particules) sans jouer de son.
        RefreshHealthVisual();
        ApplyLightVisual(); // état initial des feux (un véhicule sorti peut déjà être allumé)
        RefreshSimulationState();
    }

    public override void OnStartAuthority() => RefreshSimulationState();
    public override void OnStopAuthority()  => RefreshSimulationState();

    /// <summary>AudioSource 3D dédiée à la boucle moteur, mutualisée sur le bus SFX du mixer.</summary>
    private void SetupEngineAudio() {
        _engineSource = gameObject.AddComponent<AudioSource>();
        _engineSource.playOnAwake   = false;
        _engineSource.loop          = true;
        _engineSource.spatialBlend  = 1f;
        _engineSource.rolloffMode   = AudioRolloffMode.Linear;
        _engineSource.minDistance   = 3f;
        _engineSource.maxDistance   = 25f;
        _engineSource.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;

        _brakeSource = gameObject.AddComponent<AudioSource>();
        _brakeSource.playOnAwake   = false;
        _brakeSource.loop          = true;     // jouée tant qu'on freine, stoppée au relâché
        _brakeSource.spatialBlend  = 1f;
        _brakeSource.rolloffMode   = AudioRolloffMode.Linear;
        _brakeSource.minDistance   = 3f;
        _brakeSource.maxDistance   = 22f;
        _brakeSource.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
    }

    /// <summary>Démarre/arrête la boucle de freinage selon l'input (coupée dès qu'on ne freine plus).</summary>
    private void SetBrakeSound(bool on) {
        if (_brakeSource == null) return;
        AudioClip clip = config != null ? config.brake : null;
        if (on && clip != null) {
            if (!_brakeSource.isPlaying) { _brakeSource.clip = clip; _brakeSource.Play(); }
        } else if (_brakeSource.isPlaying) {
            _brakeSource.Stop();
        }
    }

    private void OnDestroy() {
        if (_enterAction != null) _enterAction.OnExecute -= OnEnterActionExecuted;
        if (_exitAction != null) _exitAction.OnExecute -= OnExitActionExecuted;
        if (_lockAction != null) _lockAction.OnExecute -= OnLockActionExecuted;
        if (_unlockAction != null) _unlockAction.OnExecute -= OnUnlockActionExecuted;
        if (_refuelAction != null) _refuelAction.OnExecute -= OnRefuelActionExecuted;
        if (_openTrunkAction != null) _openTrunkAction.OnExecute -= OnOpenTrunkActionExecuted;
        if (_repairAction != null) _repairAction.OnExecute -= OnRepairActionExecuted;
        if (_fuelBar != null) Destroy(_fuelBar.gameObject);
    }

    /// <summary>Le véhicule a un coffre (config.trunk est un conteneur).</summary>
    private bool HasTrunk => config != null && config.trunk != null && config.trunk.IsContainer;

    /// <summary>Id persistant servant de clé de place "container:{uuid}" pour le coffre.</summary>
    private string ServerTrunkUuid() => !string.IsNullOrEmpty(vehicleDbId) ? vehicleDbId : vehicleKey;

    // ── IInteractable ─────────────────────────────────────────────────────────────

    public float GetRange()          => interactionRange;
    public bool  IsInteractable()    => CanEnterAction() || CanExitAction() || OwnerLockActionAvailable() || CanRefuelAction() || CanOpenTrunkAction() || CanRepairAction();
    public bool  IsRightClickOnly()  => false;
    public void  StopInteraction()   { }

    /// <summary>Action « Monter » disponible : véhicule déverrouillé + place libre + le joueur local
    /// n'est PAS déjà à bord (un occupant voit « Sortir », pas « Monter »).</summary>
    private bool CanEnterAction() {
        if (_enterAction == null || isLocked || !HasFreeSeat()) return false;
        var local = PlayerController.Local;
        return local != null && !IsOccupant(local.netId);
    }

    /// <summary>Action « Sortir » disponible : le joueur local est à bord (conducteur ou passager).
    /// Affichée MÊME verrouillé : la tentative déclenche alors un toast d'erreur (cf. LocalRequestExit).</summary>
    private bool CanExitAction() {
        if (_exitAction == null) return false;
        var local = PlayerController.Local;
        return local != null && IsOccupant(local.netId);
    }

    /// <summary>Action « Mettre de l'essence » : visible dès que le joueur local tient un bidon
    /// (hint client). Le serveur valide au clic — réservoir plein / bidon vide renvoient un toast.</summary>
    private bool CanRefuelAction() {
        if (_refuelAction == null) return false;
        var local = PlayerController.Local;
        if (local == null || local.PlayerHands == null) return false;
        return IsCanister(local.PlayerHands.RightHandItem) || IsCanister(local.PlayerHands.LeftHandItem);
    }

    private static bool IsCanister(ItemBehaviour it) =>
        it != null && it.Configuration != null && it.Configuration.ID == FuelCanister.ConfigId;

    /// <summary>Action « Ouvrir le coffre » : visible dès que le véhicule a un coffre, MÊME verrouillé.
    /// Verrouillé : la tentative déclenche un toast d'erreur (cf. OnOpenTrunkActionExecuted).</summary>
    private bool CanOpenTrunkAction() => _openTrunkAction != null && HasTrunk;

    /// <summary>Action « Réparer » : visible UNIQUEMENT si le véhicule est CASSÉ (KO), pour le
    /// PROPRIÉTAIRE et depuis l'EXTÉRIEUR (le véhicule sera rangé au garage → il doit être vide).</summary>
    private bool CanRepairAction() {
        if (_repairAction == null || !IsKO) return false;
        var local = PlayerController.Local;
        if (local == null || local.CharacterData == null) return false;
        if (local.CharacterData.Id != ownerCharacterId) return false;
        return !IsOccupant(local.netId);
    }

    /// <summary>Le joueur local est le propriétaire ET à l'extérieur → action verrouiller/déverrouiller.</summary>
    private bool OwnerLockActionAvailable() {
        var local = PlayerController.Local;
        if (local == null || string.IsNullOrEmpty(ownerCharacterId)) return false;
        if (local.CharacterData == null || local.CharacterData.Id != ownerCharacterId) return false;
        if (IsOccupant(local.netId)) return false; // depuis l'extérieur uniquement
        return (isLocked ? _unlockAction : _lockAction) != null;
    }

    public bool IsOwnedBy(string characterId) => !string.IsNullOrEmpty(ownerCharacterId) && ownerCharacterId == characterId;

    public Action[] GetActions(bool withPriority = false) {
        var list = new List<Action>(4);
        if (CanEnterAction()) list.Add(_enterAction);
        if (CanExitAction()) list.Add(_exitAction);
        if (OwnerLockActionAvailable()) list.Add(isLocked ? _unlockAction : _lockAction);
        if (CanRefuelAction()) list.Add(_refuelAction);
        if (CanOpenTrunkAction()) list.Add(_openTrunkAction);
        if (CanRepairAction()) list.Add(_repairAction);
        return list.Count == 0 ? System.Array.Empty<Action>() : list.ToArray();
    }

    private void OnEnterActionExecuted(Action action) {
        if (PlayerController.Local == null) return;
        CmdEnterVehicle();
    }

    private void OnExitActionExecuted(Action action) => LocalRequestExit();

    /// <summary>Point d'entrée CLIENT unique de la sortie (clic « Sortir » OU touche X, conducteur
    /// comme passager). Verrouillé → toast d'erreur, aucune Command. Sinon dispatch conducteur /
    /// passager. isLocked est répliqué → le contrôle client est fiable (le serveur revalide aussi).</summary>
    public void LocalRequestExit() {
        var local = PlayerController.Local;
        if (local == null) return;
        if (isLocked) { WorldToastManager.ShowError("Véhicule verrouillé"); return; }
        if (driverNetId == local.netId) CmdExitVehicle();
        else CmdExitAsPassenger();
    }

    private void OnLockActionExecuted(Action action)   => CmdSetLockAsOwner(true);
    private void OnUnlockActionExecuted(Action action) => CmdSetLockAsOwner(false);
    private void OnRefuelActionExecuted(Action action) => CmdRefuel();

    /// <summary>Ouverture du coffre : verrouillé → toast d'erreur, aucune Command (le serveur revalide).</summary>
    private void OnOpenTrunkActionExecuted(Action action) {
        if (isLocked) { WorldToastManager.ShowError("Véhicule verrouillé"); return; }
        CmdOpenTrunk();
    }

    /// <summary>Réparer : ouvre une confirmation (coût + rangement garage) ; confirme → CmdRepair.</summary>
    private void OnRepairActionExecuted(Action action) {
        Sim.UI.ConfirmDialogUI.Request(
            "Réparer le véhicule ?",
            $"La réparation coûte {RepairCost} BC. Le véhicule sera réparé puis renvoyé directement au garage.",
            () => CmdRepair());
    }

    public bool IsLocked => isLocked;

    // ── Server: enter / exit ────────────────────────────────────────────────────

    [Command(requiresAuthority = false)]
    private void CmdEnterVehicle(NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        if (isLocked) return;                         // verrouillé : personne ne monte
        uint id = conn.identity.netId;
        if (IsOccupant(id)) return;                   // déjà à bord

        // Validation de proximité (anti-triche léger).
        float sqr = (conn.identity.transform.position - transform.position).sqrMagnitude;
        if (sqr > (interactionRange * 2f) * (interactionRange * 2f)) return;

        PlayerController pc = conn.identity.GetComponent<PlayerController>();

        // Conducteur si la place est libre, sinon premier siège passager disponible.
        if (driverNetId == 0) {
            netIdentity.AssignClientAuthority(conn);
            RefreshSimulationState();                  // serveur → kinematic (le conducteur simule)
            ApplyParentingTo(pc, seatAnchor, true);   // serveur
            driverNetId = id;
            ServerClaimOwnership(pc);                  // premier conducteur = propriétaire (persisté)
            return;
        }

        for (int i = 0; i < PassengerCapacity; i++) {
            if (GetPassenger(i) != 0) continue;
            ApplyParentingTo(pc, passengerSeats[i], true); // serveur
            SetPassenger(i, id);
            return;
        }
        // Véhicule plein : aucune action.
    }

    [Command(requiresAuthority = false)]
    private void CmdExitAsPassenger(NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        if (isLocked) return;                         // verrouillé : le passager ne sort pas
        uint id = conn.identity.netId;
        for (int i = 0; i < MaxPassengerSlots; i++) {
            if (GetPassenger(i) != id) continue;
            if (NetworkServer.spawned.TryGetValue(id, out NetworkIdentity idn) && i < passengerSeats.Length)
                ApplyParentingTo(idn.GetComponent<PlayerController>(), passengerSeats[i], false); // serveur
            SetPassenger(i, 0);
            return;
        }
    }

    [Command]
    private void CmdExitVehicle() {
        if (driverNetId == 0) return;
        if (isLocked) return;                         // verrouillé : on ne sort pas
        ServerReleaseDriver();
    }

    // ── Verrouillage ────────────────────────────────────────────────────────────────

    /// <summary>Bascule par le conducteur (autorité du véhicule) — propriétaire ou non.</summary>
    [Command]
    private void CmdToggleLockAsDriver() {
        isLocked = !isLocked;
    }

    /// <summary>Verrouille/déverrouille par le propriétaire depuis l'extérieur.</summary>
    [Command(requiresAuthority = false)]
    private void CmdSetLockAsOwner(bool locked, NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        PlayerController pc = conn.identity.GetComponent<PlayerController>();
        string charId = pc != null && pc.CharacterData != null ? pc.CharacterData.Id : null;
        if (string.IsNullOrEmpty(charId) || charId != ownerCharacterId) return; // propriétaire uniquement
        float sqr = (conn.identity.transform.position - transform.position).sqrMagnitude;
        if (sqr > (interactionRange * 2f) * (interactionRange * 2f)) return;
        isLocked = locked;
    }

    // ── Propriété persistée (serveur) ───────────────────────────────────────────────

    /// <summary>Hydrate la propriété depuis la DB au spawn (appelé par VehicleSpawner). La clé
    /// stable permet de retrouver le propriétaire à chaque (re)spawn. Pas de position persistée.</summary>
    [Server]
    public void ServerInitOwnership(string key) {
        if (_ownershipInitialized) return;
        _ownershipInitialized = true;
        vehicleKey = key;
        if (string.IsNullOrEmpty(key) || ApiManager.Instance == null) return;
        string mdl = config != null ? config.id : "vehicle"; // on stocke l'id de config dans `model`
        ApiManager.Instance.StartCoroutine(
            ApiManager.Instance.EnsureVehicleCoroutine(key, mdl, row => {
                if (row != null) ownerCharacterId = row.ownerCharacterId ?? "";
            }));
    }

    /// <summary>Initialise un véhicule SORTI d'un garage : propriétaire connu + id DB (pour le rangement).
    /// Pas d'appel DB (la propriété est déjà persistée).</summary>
    [Server]
    public void ServerInitFromGarage(string dbId, string ownerCharId, float restoredHealth, float restoredFuel) {
        _ownershipInitialized = true;
        vehicleDbId = dbId ?? "";
        ownerCharacterId = ownerCharId ?? "";
        // Restaure l'état persisté (-1 = jamais sauvegardé → plein). Précède OnStartServer.
        health = restoredHealth >= 0f ? Mathf.Min(restoredHealth, MaxHealth) : MaxHealth;
        fuel   = restoredFuel   >= 0f ? Mathf.Min(restoredFuel,   MaxFuel)   : MaxFuel;
        _stateInitialized = true;
    }

    /// <summary>Le premier conducteur réclame la propriété (persistée), si le véhicule est libre.</summary>
    [Server]
    private void ServerClaimOwnership(PlayerController driver) {
        if (!string.IsNullOrEmpty(ownerCharacterId)) return; // déjà possédé
        string charId = driver != null && driver.CharacterData != null ? driver.CharacterData.Id : null;
        if (string.IsNullOrEmpty(charId)) return;
        ownerCharacterId = charId;
        if (!string.IsNullOrEmpty(vehicleKey) && ApiManager.Instance != null)
            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.SetVehicleOwnerCoroutine(vehicleKey, charId, _ => { }));
    }

    // ── Feux (phares + stop) ─────────────────────────────────────────────────────────

    /// <summary>Le conducteur (autorité du véhicule) pousse l'état des feux voulu.</summary>
    [Command]
    private void CmdSetLights(byte flags) => lightFlags = flags;

    private void OnLightFlagsChanged(byte previous, byte current) => ApplyLightVisual();

    private void ApplyLightVisual() {
        if (_lights == null) return;
        _lights.SetHeadlights((lightFlags & LIGHT_HEAD)  != 0);
        _lights.SetBrake     ((lightFlags & LIGHT_BRAKE) != 0);
    }

    // ── Carburant ──────────────────────────────────────────────────────────────────

    /// <summary>Le conducteur consomme localement et envoie la consommation par paquets au serveur.</summary>
    [Command]
    private void CmdConsumeFuel(float amount) {
        if (fuel < 0f) fuel = MaxFuel;          // sécurité si pas encore initialisé
        fuel = Mathf.Max(0f, fuel - Mathf.Max(0f, amount));
    }

    /// <summary>Refait le plein (station-service à venir). Serveur uniquement.</summary>
    [Server]
    public void ServerRefuel(float amount) {
        if (fuel < 0f) fuel = 0f;
        fuel = Mathf.Clamp(fuel + Mathf.Max(0f, amount), 0f, MaxFuel);
    }

    /// <summary>« Mettre de l'essence » : transfère le contenu du bidon tenu vers le réservoir.</summary>
    [Command(requiresAuthority = false)]
    private void CmdRefuel(NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        float sqr = (conn.identity.transform.position - transform.position).sqrMagnitude;
        if (sqr > (interactionRange * 2f) * (interactionRange * 2f)) return;

        if (fuel < 0f) fuel = MaxFuel;
        float deficit = MaxFuel - fuel;
        if (deficit <= 0.01f) { TargetRefuelResult(conn, false, "Réservoir plein"); return; }

        bool transferred = ServerItemManager.Instance.TryConsumeHeldFuel(
            conn, FuelCanister.ConfigId, deficit, out float amount, out bool hasItem);
        if (!hasItem)     { TargetRefuelResult(conn, false, "Aucun bidon"); return; }
        if (!transferred) { TargetRefuelResult(conn, false, "Bidon vide");  return; }

        ServerRefuel(amount);
        TargetRefuelResult(conn, true, $"+{amount:0} L");
    }

    /// <summary>Résultat du ravitaillement renvoyé à l'initiateur (toast succès/erreur).</summary>
    [TargetRpc]
    private void TargetRefuelResult(NetworkConnectionToClient target, bool success, string message) {
        if (success) WorldToastManager.ShowSuccess(message);
        else         WorldToastManager.ShowError(message);
    }

    /// <summary>Répare le véhicule cassé (KO) : débite le propriétaire de <see cref="repairCost"/>,
    /// remet la vie au max, puis range le véhicule au garage (despawn → OnStopServer persiste vie=max ;
    /// la place garage DB du véhicule est conservée). Propriétaire + extérieur + proximité requis.</summary>
    [Command(requiresAuthority = false)]
    private void CmdRepair(NetworkConnectionToClient conn = null) {
        if (conn?.identity == null) return;
        if (!IsKO) return;                       // seulement si cassé
        if (IsOccupied) return;                  // doit être vide (il part au garage)
        PlayerController pc = conn.identity.GetComponent<PlayerController>();
        string charId = pc != null && pc.CharacterData != null ? pc.CharacterData.Id : null;
        if (string.IsNullOrEmpty(charId) || charId != ownerCharacterId) return; // propriétaire uniquement
        float sqr = (conn.identity.transform.position - transform.position).sqrMagnitude;
        if (sqr > (interactionRange * 2f) * (interactionRange * 2f)) return;

        PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
        if (bank == null) return;
        if (bank.Money < RepairCost) { TargetRepairResult(conn, false, "Fonds insuffisants"); return; }

        bank.PostLedger(-RepairCost, Sim.Entities.Persistence.LedgerReason.VehicleRepair,
                        Sim.Entities.Persistence.LedgerCounterparty.System, "GARAGE");

        health = MaxHealth;                      // réparé : la persistance au despawn écrira vie=max
        TargetRepairResult(conn, true, "Véhicule réparé et rangé au garage");
        NetworkServer.Destroy(gameObject);       // rangé au garage (place DB conservée)
    }

    [TargetRpc]
    private void TargetRepairResult(NetworkConnectionToClient target, bool success, string message) {
        if (success) WorldToastManager.ShowSuccess(message);
        else         WorldToastManager.ShowError(message);
    }

    /// <summary>Ouvre le coffre : réutilise le système de conteneurs (ContainerUI). Accès =
    /// déverrouillé pour tous, sinon propriétaire ; validé côté serveur + proximité.</summary>
    [Command(requiresAuthority = false)]
    private void CmdOpenTrunk(NetworkConnectionToClient conn = null) {
        if (conn?.identity == null || !HasTrunk) return;
        if (isLocked) return; // verrouillé : coffre inaccessible (le client affiche le toast)
        float sqr = (conn.identity.transform.position - transform.position).sqrMagnitude;
        if (sqr > (interactionRange * 2f) * (interactionRange * 2f)) return;
        ServerItemManager.Instance.OpenVehicleTrunk(conn, ServerTrunkUuid(), config.trunk);
    }

    private void OnFuelChanged(float previous, float current) {
        // Panne sèche : coupe le moteur + son de réservoir vide (le HUD lit FuelNormalized chaque frame).
        if (current == 0f && previous != 0f) {
            StopEngine();
            if (previous > 0f && config != null && config.fuelEmpty != null)
                AudioManager.Instance.PlayClip3D(config.fuelEmpty, transform.position);
        }

        // Ravitaillement (carburant en HAUSSE, hors init -1→plein) : jauge monde « Réservoir » + SFX.
        if (previous >= 0f && current > previous + 0.001f) {
            ShowFuelBar();
            if (config != null && config.refuel != null)
                AudioManager.Instance.PlayClip3D(config.refuel, transform.position);
        }
    }

    private void ShowFuelBar() {
        if (_fuelBar == null) _fuelBar = VehicleFuelBar.Create(transform, FuelBarHeight);
        _fuelBar.SetProgress(FuelNormalized);
    }

    private void OnLockedChanged(bool previous, bool current) {
        AudioClip clip = config != null ? (current ? config.lockSound : config.unlockSound) : null;
        if (clip != null) AudioManager.Instance.PlayClip3D(clip, transform.position);
    }

    [Server]
    private void ServerReleaseDriver() {
        if (NetworkServer.spawned.TryGetValue(driverNetId, out NetworkIdentity idn)) {
            ApplyParentingTo(idn.GetComponent<PlayerController>(), seatAnchor, false); // serveur
        }
        if (netIdentity.connectionToClient != null) netIdentity.RemoveClientAuthority();
        RefreshSimulationState();                      // serveur reprend la simulation (véhicule garé)
        _currentSpeed = 0f;
        _velocity = Vector3.zero;
        lightFlags = 0; // phares + stop éteints quand le conducteur descend
        driverNetId = 0;
    }

    // ── SyncVar hook (clients, host inclus) ─────────────────────────────────────

    private void OnDriverChanged(uint previous, uint current) {
        if (current != 0) {
            PlayDoorSound(true);   // portière qui s'ouvre
            StartEngine();
            PlayerController driver = ResolveClientPlayer(current);
            if (driver == null) return;
            ApplyParentingTo(driver, seatAnchor, true);
            if (driver == PlayerController.Local) {
                _desiredFlags = lightFlags; // synchronise l'état voulu sur l'état répliqué courant
                driver.DriveVehicle(this);
            }
        }
        else if (previous != 0) {
            PlayDoorSound(false);  // portière qui se ferme
            StopEngine();
            PlayerController driver = ResolveClientPlayer(previous);
            if (driver == null) return;
            ApplyParentingTo(driver, seatAnchor, false);
            if (driver == PlayerController.Local) driver.Idle(); // → CharacterDrive.OnExit
        }
    }

    // ── Hooks passagers (clients, host inclus) ──────────────────────────────────────

    private void OnPassenger0Changed(uint prev, uint cur) => OnPassengerSeatChanged(0, prev, cur);
    private void OnPassenger1Changed(uint prev, uint cur) => OnPassengerSeatChanged(1, prev, cur);
    private void OnPassenger2Changed(uint prev, uint cur) => OnPassengerSeatChanged(2, prev, cur);

    private void OnPassengerSeatChanged(int seat, uint prev, uint cur) {
        if (passengerSeats == null || seat >= passengerSeats.Length) return;
        Transform anchor = passengerSeats[seat];
        if (cur != 0) {
            PlayDoorSound(true);
            PlayerController p = ResolveClientPlayer(cur);
            if (p == null) return;
            ApplyParentingTo(p, anchor, true);
            if (p == PlayerController.Local) p.RidePassenger(this);
        }
        else if (prev != 0) {
            PlayDoorSound(false);
            PlayerController p = ResolveClientPlayer(prev);
            if (p == null) return;
            ApplyParentingTo(p, anchor, false);
            if (p == PlayerController.Local) p.Idle(); // → CharacterPassenger.OnExit
        }
    }

    // ── Audio (tous clients) ────────────────────────────────────────────────────────

    private void PlayDoorSound(bool opening) {
        AudioClip clip = config != null ? (opening ? config.doorOpen : config.doorClose) : null;
        if (clip != null) AudioManager.Instance.PlayClip3D(clip, transform.position);
    }

    private void StartEngine() {
        if (IsKO || !HasFuel) return;
        AudioClip loop = config != null ? config.engineLoop : null;
        if (loop == null || _engineSource == null) return;
        _engineSource.clip = loop;
        _engineSource.pitch = enginePitchIdle;
        _engineSource.Play();
    }

    private void StopEngine() {
        if (_engineSource != null) _engineSource.Stop();
        SetBrakeSound(false); // coupe aussi le frein (sortie / KO)
    }

    /// <summary>Pitch moteur suivant la vitesse. Le conducteur (owner) a _currentSpeed réel ;
    /// les clients distants restent au pitch ralenti (acceptable pour le POC).</summary>
    private void UpdateEngineAudio() {
        if (_engineSource == null || !_engineSource.isPlaying) return;
        _engineSource.pitch = Mathf.Lerp(enginePitchIdle, enginePitchMax, NormalizedSpeed);
    }

    // ── Santé / collisions ──────────────────────────────────────────────────────────

    private void OnHealthChanged(float previous, float current) {
        RefreshHealthVisual();

        // Son de choc proportionnel aux dégâts subis (ignore l'init -1 → MaxHealth).
        if (previous >= 0f && current < previous && config != null && config.impact != null) {
            float severity = Mathf.Clamp01((previous - current) / Mathf.Max(1f, MaxHealth) * 3f);
            AudioManager.Instance.PlayClip3D(config.impact, transform.position, 0.4f + 0.6f * severity);
        }

        // Passage KO.
        if (current == 0f && previous != 0f) {
            StopEngine();
            if (config != null && config.ko != null)
                AudioManager.Instance.PlayClip3D(config.ko, transform.position);
        }
    }

    /// <summary>Met à jour les particules KO d'après la vie courante (la barre de vie vit dans le HUD).</summary>
    private void RefreshHealthVisual() {
        SetKoParticles(IsKO);
    }

    private void SetKoParticles(bool on) {
        if (koParticles == null) return;
        if (on) { if (!koParticles.isPlaying) koParticles.Play(); }
        else    { if (koParticles.isPlaying)  koParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
    }

    /// <summary>Applique un impact côté serveur (appelé par le conducteur via Command).</summary>
    [Server]
    private void ServerApplyImpact(float impactSpeed) {
        if (health <= 0f) return;
        float over = impactSpeed - minImpactSpeed;
        if (over <= 0f) return;
        float damage = over * impactDamageFactor;
        health = Mathf.Max(0f, health - damage);
        if (health == 0f) lightFlags &= unchecked((byte)~LIGHT_BRAKE); // KO : feux stop éteints
    }

    [Command(requiresAuthority = false)]
    private void CmdReportImpact(float impactSpeed) => ServerApplyImpact(impactSpeed);

    // ── Klaxon (réseau) ─────────────────────────────────────────────────────────────

    [Command]
    private void CmdHorn() => RpcHorn();

    [ClientRpc]
    private void RpcHorn() {
        if (config != null && config.horn != null)
            AudioManager.Instance.PlayClip3D(config.horn, transform.position);
    }

    private static PlayerController ResolveClientPlayer(uint id) {
        if (NetworkClient.spawned.TryGetValue(id, out NetworkIdentity idn))
            return idn.GetComponent<PlayerController>();
        return null;
    }

    // ── Parentage partagé (serveur + clients) ───────────────────────────────────

    /// <summary>
    /// Parente (ou dé-parente) le joueur sous un siège donné (conducteur ou passager). Doit être
    /// appliqué identiquement partout pour que le NetworkTransform (position locale) reste cohérent.
    /// Idempotent : ré-appliquer le même parent est sans effet.
    /// </summary>
    private void ApplyParentingTo(PlayerController player, Transform seat, bool attach) {
        if (player == null || seat == null) return;
        if (attach) {
            player.transform.SetParent(seat, worldPositionStays: false);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
        }
        else {
            if (player.transform.parent == seat)
                player.transform.SetParent(null, worldPositionStays: true);
        }
    }

    // ── Driving (owner only) ─────────────────────────────────────────────────────

    private void Update() {
        if (isServer) ServerWatchdog();
        UpdateEngineAudio();          // tous clients (pitch moteur)
        if (!isOwned)  return;

        if (Input.GetKeyDown(KeyCode.X)) {
            LocalRequestExit();
            return;
        }

        if (Input.GetKeyDown(KeyCode.H)) CmdHorn();
        if (Input.GetKeyDown(KeyCode.L)) CmdToggleLockAsDriver(); // clé : verrouiller/déverrouiller
        if (Input.GetKeyDown(KeyCode.F)) _desiredFlags ^= LIGHT_HEAD; // phares on/off

        // Feux stop = frein tenu (réconcilie l'état voulu, edge-triggered) + boucle sonore de frein.
        bool braking = Input.GetKey(KeyCode.Space);
        if (braking) _desiredFlags |= LIGHT_BRAKE; else _desiredFlags &= unchecked((byte)~LIGHT_BRAKE);
        if (_desiredFlags != lightFlags) CmdSetLights(_desiredFlags);
        SetBrakeSound(braking && CurrentSpeedMs > 1f);

        DetectCharacterHits();
    }

    /// <summary>Conduite physique (WheelCollider) — conducteur (autorité) uniquement, en pas physique.
    /// Couple moteur + frein sur les 4 roues, braquage lissé sur les 2 avant, limitation de vitesse,
    /// consommation carburant. La suspension/le relief sont gérés nativement par les WheelColliders.</summary>
    private void FixedUpdate() {
        if (!isOwned || _rb == null || _rb.isKinematic || wheelColliders == null || wheelColliders.Length < 4) return;

        // Cache pour HUD / détection de renversement / audio (vitesse planaire + composante avant signée).
        _velocity = Vector3.ProjectOnPlane(_rb.linearVelocity, transform.up);
        _currentSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);

        float throttle  = (IsKO || !HasFuel) ? 0f : Input.GetAxisRaw("Vertical");
        float steerIn   = Input.GetAxisRaw("Horizontal");
        bool  spaceHeld = Input.GetKey(KeyCode.Space);
        bool  handbrake = spaceHeld || IsKO;

        // Braquage lissé → roues avant (indices 0,1). Alimente aussi le visuel des roues.
        float targetSteer = steerIn * maxSteerAngleDeg;
        _steerAngleCurrent = Mathf.Lerp(_steerAngleCurrent, targetSteer, steerLerp * Time.fixedDeltaTime);
        if (wheelColliders[0] != null) wheelColliders[0].steerAngle = _steerAngleCurrent;
        if (wheelColliders[1] != null) wheelColliders[1].steerAngle = _steerAngleCurrent;
        if (_wheels != null) _wheels.SetSteerInput(maxSteerAngleDeg > 0f ? _steerAngleCurrent / maxSteerAngleDeg : 0f);

        // Adhérence latérale : avant = frontGrip ; arrière = rearGrip, réduit au frein à main → DÉRAPAGE.
        SetSideGrip(0, frontGrip);
        SetSideGrip(1, frontGrip);
        float rearStiffness = (spaceHeld && !IsKO) ? handbrakeRearGrip : rearGrip;
        SetSideGrip(2, rearStiffness);
        SetSideGrip(3, rearStiffness);

        // Couple moteur / frein selon l'intention et le sens de marche.
        float speedMs  = _currentSpeed;     // signé : avant = +
        float topSpeed = MaxSpeed;
        float motor = 0f, brake = handbrake ? brakeTorque : 0f;
        if (!handbrake) {
            if (!HasFuel) {
                // PANNE SÈCHE : plus de couple moteur ET pas de frein moteur → roue libre, le véhicule
                // continue de rouler et s'arrête NATURELLEMENT (résistance des roues + traînée).
                brake = 0f;
            } else if (throttle > 0.01f) {
                if (speedMs < -0.5f) brake = brakeTorque;                 // recule mais veut avancer → freiner
                else if (speedMs < topSpeed) motor = throttle * motorTorque;
            } else if (throttle < -0.01f) {
                if (speedMs > 0.5f) brake = brakeTorque;                  // avance mais veut reculer → freiner
                else if (-speedMs < topSpeed * reverseSpeedFactor) motor = throttle * motorTorque;
            } else {
                brake = engineBrakeTorque;                                // roue libre : frein moteur léger
            }
        }
        for (int i = 0; i < wheelColliders.Length; i++) {
            WheelCollider w = wheelColliders[i];
            if (w == null) continue;
            w.motorTorque = motor;
            w.brakeTorque = brake;
        }

        // Consommation carburant (par paquets envoyés au serveur).
        if (HasFuel && config != null) {
            float rate = config.fuelIdleConsumption
                       + Mathf.Max(0f, config.fuelConsumption - config.fuelIdleConsumption) * NormalizedSpeed;
            _fuelAccum += Mathf.Max(0f, rate) * Time.fixedDeltaTime;
            _fuelSendTimer += Time.fixedDeltaTime;
            if (_fuelSendTimer >= 0.5f && _fuelAccum > 0f) {
                CmdConsumeFuel(_fuelAccum);
                _fuelAccum = 0f;
                _fuelSendTimer = 0f;
            }
        }
    }

    /// <summary>Règle la rigidité de friction latérale d'une roue (adhérence/dérapage).</summary>
    private void SetSideGrip(int i, float stiffness) {
        if (wheelColliders == null || i >= wheelColliders.Length || wheelColliders[i] == null) return;
        WheelFrictionCurve f = wheelColliders[i].sidewaysFriction;
        f.stiffness = stiffness;
        wheelColliders[i].sidewaysFriction = f;
    }

    /// <summary>Dégâts d'impact via la physique (conducteur uniquement). Les chocs « mous » (sol,
    /// personnages — gérés par le renversement) sont ignorés via le masque + le seuil de vitesse.</summary>
    private void OnCollisionEnter(Collision collision) {
        if (!isOwned || collision == null) return;
        if ((_impactMask & (1 << collision.gameObject.layer)) == 0) return;
        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed <= minImpactSpeed || Time.time - _lastCollisionDamageTime < 0.3f) return;
        _lastCollisionDamageTime = Time.time;
        CmdReportImpact(impactSpeed);
        if (config != null && config.impact != null)
            AudioManager.Instance.PlayClip3D(config.impact, transform.position);
    }

    /// <summary>Conducteur uniquement : détecte les personnages (joueurs/PNJ) percutés par le
    /// véhicule en mouvement et les signale au serveur pour un renversement (ragdoll). NE stoppe PAS
    /// le véhicule (on plonge à travers). Cooldown par cible pour éviter le spam d'impacts.</summary>
    private void DetectCharacterHits() {
        if (_characterMask == 0) return;
        float speed = _velocity.magnitude;
        if (speed < minKnockdownSpeed) return;

        int count = Physics.OverlapBoxNonAlloc(transform.position, sweepHalfExtents, _hitBuffer,
                                               transform.rotation, _characterMask, QueryTriggerInteraction.Ignore);
        if (count == 0) return;

        Vector3 dir = speed > 0.01f ? _velocity / speed : transform.forward;
        Vector3 impulse = (dir + Vector3.up * 0.5f).normalized * (speed * knockImpulsePerSpeed);

        for (int i = 0; i < count; i++) {
            Collider c = _hitBuffer[i];
            if (c == null || c.transform.IsChildOf(transform)) continue;
            Vector3 point = c.ClosestPoint(transform.position);

            NetworkIdentity ni = c.GetComponentInParent<NetworkIdentity>();
            if (ni != null) {
                if (OnCooldown(_playerHitTime, ni.netId)) continue;
                CmdReportCharacterHit(ni.netId, true, impulse, point);
                continue;
            }
            ClientNpcView npc = c.GetComponentInParent<ClientNpcView>();
            if (npc != null && !OnCooldown(_npcHitTime, npc.NpcId)) {
                CmdReportCharacterHit((uint)npc.NpcId, false, impulse, point);
            }
        }
    }

    private bool OnCooldown<TKey>(Dictionary<TKey, float> map, TKey key) {
        if (map.TryGetValue(key, out float last) && Time.time - last < characterHitCooldown) return true;
        map[key] = Time.time;
        return false;
    }

    /// <summary>Relaie le renversement au serveur, qui déclenche le ragdoll réseau sur la cible
    /// (joueur via PlayerController, PNJ via NpcAIController). Le véhicule continue sa route.</summary>
    [Command(requiresAuthority = false)]
    private void CmdReportCharacterHit(uint id, bool isPlayer, Vector3 impulse, Vector3 point) {
        if (driverNetId == 0) return; // un véhicule sans conducteur ne renverse personne
        if (isPlayer) {
            if (NetworkServer.spawned.TryGetValue(id, out NetworkIdentity idn)) {
                PlayerController pc = idn.GetComponent<PlayerController>();
                if (pc != null) pc.ServerKnockDown(impulse, point);
            }
        } else if (NpcAIController.TryGet((int)id, out NpcAIController npc)) {
            npc.ServerKnockDown(impulse, point);
        }
    }

    [Server]
    private void ServerWatchdog() {
        // Si le conducteur s'est déconnecté (identity disparue), libère le véhicule.
        if (driverNetId != 0 && !NetworkServer.spawned.ContainsKey(driverNetId)) {
            _currentSpeed = 0f;
            _velocity = Vector3.zero;
            driverNetId = 0;
        }
        // Idem pour les passagers déconnectés.
        for (int i = 0; i < MaxPassengerSlots; i++) {
            uint id = GetPassenger(i);
            if (id != 0 && !NetworkServer.spawned.ContainsKey(id)) SetPassenger(i, 0);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Position de descente échantillonnée sur le NavMesh près de l'exitAnchor.</summary>
    public Vector3 GetExitPosition() {
        Vector3 candidate = exitAnchor != null ? exitAnchor.position
                                               : transform.position + transform.right * 2f;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            return hit.position;
        return candidate;
    }
}
