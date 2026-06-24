using Mirror;
using Sim;
using Sim.Building;
using Sim.Enums;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

/// <summary>
/// Contrôleur LOCAL de la « pose » d'un item tenu (action PLACE). Le placement réutilise
/// EXACTEMENT le système de build des props : un <see cref="BuildPreview"/> est ajouté au ghost
/// (mêmes collisions, mêmes surfaces posables) et un HUD dédié minimal — une simple image
/// « Mode Pose » (PanelTypeEnum.POSE, sans bouton Valider/Annuler) — est affiché. La validité
/// n'est PLUS rendue en teintant le renderer : <see cref="BuildPreview"/> affiche une pastille
/// au-dessus de l'objet et un outline vert/rouge (<see cref="Sim.Building.PlacementFeedback"/>).
/// Puis, à la validation, le personnage marche jusqu'au point de pose avant que l'item ne quitte
/// la main (cf. <see cref="AI.States.CharacterPoser"/>).
///
/// Cycle :
///   Idle → Begin() → Positioning (ghost + BuildPreview, BUILD camera + HUD « Mode Pose »,
///     validation = clic-gauche / Entrée, uniquement si BuildPreview.IsPlaceable()) →
///   PendingStart (1 frame : retour FREE + StartItemPose) → Walking (le perso marche) →
///   arrivée → envoi C2S_PoseHeldItem (l'item se détache et apparaît à l'emplacement) → Idle.
///
/// Annulation à TOUT moment :
///   - Pendant Positioning : clic-droit / Échap → ghost détruit, retour FREE, item gardé.
///   - Pendant Walking : tout autre state qui prend la main (clic-déplacement, interaction) →
///     CharacterPoser.OnExit sans complétion → onCancel ici → la requête n'est jamais envoyée.
///
/// Le BuildPreview gère lui-même la détection de collision (l'item ne traverse plus les objets)
/// et la surface posable (sol / Posable Surface). En placement « libre » (item, pas de
/// PropBehaviourBase) il n'exige aucune Buildable Area : validité = pas de collision + sol dessous.
///
/// Singleton paresseux DontDestroyOnLoad (créé par code, aucun prefab à éditer).
/// </summary>
public class ItemPlacementController : MonoBehaviour
{
    private enum Phase { Idle, Positioning, PendingStart, Walking }

    // ── Tuning ────────────────────────────────────────────────────────────────
    // Rayon pour snapper le point de marche calculé vers la NavMesh la plus proche.
    private const float StandSampleRadius = 2.0f;
    // Distance d'arrêt EN DEÇA de l'emplacement (vers le joueur) : le personnage s'arrête à
    // bout de bras et pose l'objet devant lui au lieu de marcher dessus (plus réaliste).
    private const float StandOffset       = 0.8f;
    private const float CursorRayLength   = 200f;

    private Phase    _phase = Phase.Idle;
    private ItemBehaviour _item;
    private HandType _hand;

    private GameObject _ghost;
    private BuildPreview _preview;

    // Emplacement choisi, retenu pendant la marche jusqu'à l'arrivée.
    private Vector3    _pendingPos;
    private Quaternion _pendingRot;
    private Vector3    _pendingStandPoint;

    private static int _poseSurfaceMask = -1;

    // ── Singleton ─────────────────────────────────────────────────────────────
    private static ItemPlacementController _instance;
    public static ItemPlacementController Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject(nameof(ItemPlacementController));
                _instance = go.AddComponent<ItemPlacementController>();
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Entrée ────────────────────────────────────────────────────────────────

    /// <summary>Démarre la pose de l'item tenu (appelé par ItemBehaviour sur l'action PLACE, joueur local).</summary>
    public void Begin(ItemBehaviour item)
    {
        if (item == null || !item.IsHeld) return;
        PlayerController player = PlayerController.Local;
        if (player == null) return;

        // Reset propre si une pose était déjà en cours.
        if (_phase == Phase.Walking)
            player.Idle();        // interrompt la marche → CharacterPoser.OnExit → OnPoseCancelled → Idle
        else if (_phase != Phase.Idle)
            Abort();

        _item = item;
        _hand = item.HolderHand;

        HUDManager.Instance?.CloseInventory();

        SpawnGhost(item.Configuration.ID, player.transform.position);

        player.SetState(StateType.PLACING_ITEM);          // → caméra BUILD + click-to-move suspendu
        HUDManager.Instance?.DisplayPanel(PanelTypeEnum.POSE); // → HUD dédié « Mode Pose » (image only)
        _phase = Phase.Positioning;
    }

    // ── Boucle ────────────────────────────────────────────────────────────────

    private void Update()
    {
        switch (_phase)
        {
            case Phase.Positioning: TickPositioning(); break;
            case Phase.PendingStart: TickPendingStart(); break;
        }
    }

    private void TickPositioning()
    {
        PlayerController player = PlayerController.Local;
        if (player == null || _item == null || !_item.IsHeld || _ghost == null) { Abort(); return; }

        Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
        if (cam == null) return;

        // Le ghost suit le curseur tant qu'on n'est pas au-dessus de l'UI (sinon il « collerait »
        // au pointeur quand on va cliquer le bouton Valider). Même garde que BuildManager.PropsPosing.
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (!overUI)
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, CursorRayLength, PoseSurfaceMask()))
            {
                // +0.01 en Y : évite le z-fighting et garde le raycast « sol » de BuildPreview
                // (depuis transform.position vers le bas) bien au-dessus de la surface. Même
                // recette que BuildManager.CalculatePlacement pour les props au sol.
                if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Posable Surface"))
                {
                    // La surface posable appartient à un prop (table, étagère…). Ce prop est le
                    // SUPPORT, pas un obstacle : on le signale au BuildPreview pour qu'il ignore ses
                    // colliders (sinon le corps du prop bloque sa propre surface → toujours rouge).
                    var supportProp = hit.collider.GetComponentInParent<PropBehaviourBase>();
                    if (_preview != null)
                        _preview.freeSupport = supportProp != null ? supportProp.transform : hit.collider.transform.root;
                    _ghost.transform.position = new Vector3(hit.point.x, hit.point.y + (hit.normal.y * 0.01f), hit.point.z);
                }
                else
                {
                    if (_preview != null) _preview.freeSupport = null;
                    _ghost.transform.position = new Vector3(hit.point.x, hit.point.y + 0.01f, hit.point.z);
                }
            }
        }

        // Échap = annulation, même au-dessus de l'UI.
        if (Input.GetKeyDown(KeyCode.Escape)) { Abort(); return; }

        // Entrée = validation si l'emplacement est posable (raccourci clavier, comme le build).
        if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && IsPlaceable())
        {
            Confirm();
            return;
        }

        // Clics ignorés au-dessus de l'UI (le bouton du HUD gère ses propres clics).
        if (overUI) return;

        if (Input.GetMouseButtonUp(1)) { Abort(); return; }

        // Confirmation sur le RELÂCHEMENT du clic gauche (bouton lâché = pas de MoveTo parasite au
        // retour en mode FREE, cf. CameraManager.ManageInteraction qui exige GetMouseButton(0)).
        if (Input.GetMouseButtonUp(0) && IsPlaceable()) Confirm();
    }

    private bool IsPlaceable() => _preview != null && _preview.IsPlaceable();

    private void Confirm()
    {
        _pendingPos        = _ghost.transform.position;
        _pendingRot        = _ghost.transform.rotation;
        _pendingStandPoint = ComputeStandPoint(_pendingPos);

        DestroyGhost();
        HUDManager.Instance?.DisplayPanel(PanelTypeEnum.DEFAULT);
        // Bascule FREE + démarrage de la marche différés d'une frame (handoff caméra propre).
        _phase = Phase.PendingStart;
    }

    /// <summary>Point de marche : on s'arrête à <see cref="StandOffset"/> mètres EN DEÇA de
    /// l'emplacement choisi (dans la direction du joueur), de sorte que le personnage termine sa
    /// marche un peu avant et pose l'objet devant lui. Si le joueur est déjà plus proche que cet
    /// offset, on reste sur place (jamais de recul). Le résultat est snappé sur la NavMesh.</summary>
    private Vector3 ComputeStandPoint(Vector3 placementPoint)
    {
        Vector3 target = placementPoint;

        PlayerController player = PlayerController.Local;
        if (player != null)
        {
            Vector3 toPlayer = player.transform.position - placementPoint;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            if (dist > 0.0001f)
            {
                // Clamp à la distance joueur→emplacement pour ne jamais reculer au-delà du joueur.
                float offset = Mathf.Min(StandOffset, dist);
                target = placementPoint + (toPlayer / dist) * offset;
            }
        }

        if (NavMesh.SamplePosition(target, out NavMeshHit navHit, StandSampleRadius, NavMesh.AllAreas))
            return navHit.position;
        return target;
    }

    private void TickPendingStart()
    {
        PlayerController player = PlayerController.Local;
        if (player == null) { ResetState(); return; }

        player.SetState(StateType.FREE); // retour caméra/locomotion normales avant la marche

        if (player.StartItemPose(_pendingStandPoint, OnArrive, OnPoseCancelled))
        {
            _phase = Phase.Walking;
        }
        else
        {
            WorldToastManager.ShowError("Emplacement inaccessible");
            ResetState();
        }
    }

    // ── Callbacks CharacterPoser ────────────────────────────────────────────────

    /// <summary>Le personnage est arrivé : on demande au serveur de détacher et poser l'item.</summary>
    private void OnArrive()
    {
        NetworkClient.Send(new C2S_PoseHeldItem
        {
            Hand     = _hand,
            Position = _pendingPos,
            Rotation = _pendingRot
        });
        ResetState();
    }

    /// <summary>La marche a été interrompue (déplacement / interaction) : l'item reste en main.</summary>
    private void OnPoseCancelled()
    {
        ResetState();
    }

    // ── Annulation / cleanup ────────────────────────────────────────────────────

    /// <summary>Annulation pendant le positionnement : ghost détruit, retour FREE, item gardé.</summary>
    private void Abort()
    {
        DestroyGhost();
        HUDManager.Instance?.DisplayPanel(PanelTypeEnum.DEFAULT);
        PlayerController.Local?.SetState(StateType.FREE);
        ResetState();
    }

    private void ResetState()
    {
        _phase = Phase.Idle;
        _item  = null;
    }

    private void OnDisable()
    {
        DestroyGhost();
    }

    // ── Ghost ────────────────────────────────────────────────────────────────

    private void SpawnGhost(int configId, Vector3 spawnPos)
    {
        GameObject prefab = DatabaseManager.GetItemPrefab(configId);
        if (prefab == null) return;

        _ghost = Instantiate(prefab, spawnPos, Quaternion.identity);
        _ghost.name = "ItemPlacementGhost";

        // Ghost « build-like » : on garde les colliders (en trigger) pour que BuildPreview détecte
        // les collisions, mais on retire physique/logique. On désactive les MonoBehaviours plutôt
        // que de les détruire pour ne pas heurter les RequireComponent (ItemIdentity, etc.).
        foreach (var rb in _ghost.GetComponentsInChildren<Rigidbody>(true))       Destroy(rb);
        foreach (var no in _ghost.GetComponentsInChildren<NavMeshObstacle>(true)) Destroy(no);
        foreach (var b  in _ghost.GetComponentsInChildren<MonoBehaviour>(true))   if (b != null) b.enabled = false;

        // Tous les colliders du ghost en trigger : indispensable pour que BuildPreview reçoive
        // OnTriggerStay/Exit contre la géométrie solide du monde (murs, props, sol).
        foreach (var c in _ghost.GetComponentsInChildren<Collider>(true)) c.isTrigger = true;

        // Le BuildPreview ajouté APRÈS la désactivation des MonoBehaviours reste actif. En
        // placement « libre » (pas de PropBehaviourBase), il se comporte comme un prop au sol
        // sans exigence de Buildable Area. Il gère lui-même le feedback de validité (outline +
        // pastille via PlacementFeedback) — plus de teinte verte/rouge du renderer ici.
        _preview = _ghost.AddComponent<BuildPreview>();
    }

    private void DestroyGhost()
    {
        _preview = null;
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
    }

    private static int PoseSurfaceMask()
    {
        if (_poseSurfaceMask == -1) _poseSurfaceMask = LayerMask.GetMask("Ground", "Posable Surface");
        return _poseSurfaceMask;
    }
}
