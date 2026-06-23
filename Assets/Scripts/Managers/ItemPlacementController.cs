using Mirror;
using Sim;
using Sim.Enums;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

/// <summary>
/// Contrôleur LOCAL de la « pose » d'un item tenu (action PLACE). Pilote le placement fantôme
/// (ghost qui suit le curseur, vert/rouge selon l'accessibilité), puis fait marcher le personnage
/// jusqu'au point de pose avant que l'item ne quitte la main (cf. <see cref="AI.States.CharacterPoser"/>).
///
/// Cycle :
///   Idle → Begin() → Positioning (ghost, BUILD camera, clic-gauche valide = confirmer) →
///   PendingStart (1 frame : retour FREE + StartItemPose) → Walking (le perso marche) →
///   arrivée → envoi C2S_PoseHeldItem (l'item se détache et apparaît à l'emplacement) → Idle.
///
/// Annulation à TOUT moment :
///   - Pendant Positioning : clic-droit / Échap → ghost détruit, retour FREE, item gardé.
///   - Pendant Walking : tout autre state qui prend la main (clic-déplacement, interaction) →
///     CharacterPoser.OnExit sans complétion → onCancel ici → la requête n'est jamais envoyée.
///
/// Singleton paresseux DontDestroyOnLoad (créé par code, aucun prefab à éditer).
/// </summary>
public class ItemPlacementController : MonoBehaviour
{
    private enum Phase { Idle, Positioning, PendingStart, Walking }

    // ── Tuning ────────────────────────────────────────────────────────────────
    private static readonly Color ValidColor   = new Color(0.35f, 1f, 0.45f);
    private static readonly Color InvalidColor = new Color(1f, 0.35f, 0.35f);
    // Rayon de validité : le point sous le curseur est « posable » si la NavMesh passe à moins de ça.
    private const float ReachableSampleRadius = 1.0f;
    // Rayon pour snapper l'emplacement choisi vers le point NavMesh le plus proche (= point de marche).
    private const float StandSampleRadius     = 2.0f;
    private const float CursorRayLength        = 200f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private Phase    _phase = Phase.Idle;
    private ItemBehaviour _item;
    private HandType _hand;

    private GameObject _ghost;
    private Renderer[] _ghostRenderers;
    private MaterialPropertyBlock _mpb;

    // Emplacement choisi, retenu pendant la marche jusqu'à l'arrivée.
    private Vector3    _pendingPos;
    private Quaternion _pendingRot;
    private Vector3    _pendingStandPoint;

    private static int _groundMask = -1;

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

        SpawnGhost(item.Configuration.ID);

        player.SetState(StateType.PLACING_ITEM); // → caméra BUILD + click-to-move suspendu
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
        if (player == null || _item == null || !_item.IsHeld) { Abort(); return; }

        Camera cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        bool hasHit = Physics.Raycast(ray, out RaycastHit hit, CursorRayLength, GroundMask());

        bool valid = false;
        if (hasHit && _ghost != null)
        {
            _ghost.transform.SetPositionAndRotation(hit.point, Quaternion.identity);
            valid = NavMesh.SamplePosition(hit.point, out _, ReachableSampleRadius, NavMesh.AllAreas);
            TintGhost(valid ? ValidColor : InvalidColor);
        }

        // Échap = annulation, même au-dessus de l'UI.
        if (Input.GetKeyDown(KeyCode.Escape)) { Abort(); return; }

        // Clics ignorés au-dessus de l'UI.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (Input.GetMouseButtonUp(1)) { Abort(); return; }

        // Confirmation sur le RELÂCHEMENT du clic gauche (bouton lâché = pas de MoveTo parasite au
        // retour en mode FREE, cf. CameraManager.ManageInteraction qui exige GetMouseButton(0)).
        if (Input.GetMouseButtonUp(0) && hasHit && valid) Confirm(hit.point);
    }

    private void Confirm(Vector3 placementPoint)
    {
        // Point de marche = point NavMesh le plus proche de l'emplacement choisi.
        Vector3 stand = placementPoint;
        if (NavMesh.SamplePosition(placementPoint, out NavMeshHit navHit, StandSampleRadius, NavMesh.AllAreas))
            stand = navHit.position;

        _pendingPos        = placementPoint;
        _pendingRot        = Quaternion.identity;
        _pendingStandPoint = stand;

        DestroyGhost();
        // Bascule FREE + démarrage de la marche différés d'une frame (handoff caméra propre).
        _phase = Phase.PendingStart;
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

    private void SpawnGhost(int configId)
    {
        GameObject prefab = DatabaseManager.GetItemPrefab(configId);
        if (prefab == null) return;

        _ghost = Instantiate(prefab);
        _ghost.name = "ItemPlacementGhost";

        // Ghost purement visuel : pas de physique ni de logique (on désactive les MonoBehaviours
        // plutôt que de les détruire pour ne pas heurter les RequireComponent comme ItemIdentity).
        foreach (var c  in _ghost.GetComponentsInChildren<Collider>(true))        Destroy(c);
        foreach (var rb in _ghost.GetComponentsInChildren<Rigidbody>(true))       Destroy(rb);
        foreach (var no in _ghost.GetComponentsInChildren<NavMeshObstacle>(true)) Destroy(no);
        foreach (var b  in _ghost.GetComponentsInChildren<MonoBehaviour>(true))   if (b != null) b.enabled = false;

        _ghostRenderers = _ghost.GetComponentsInChildren<Renderer>(true);
        TintGhost(InvalidColor);
    }

    private void TintGhost(Color color)
    {
        if (_ghostRenderers == null) return;
        _mpb ??= new MaterialPropertyBlock();
        _mpb.SetColor(BaseColorId, color);
        foreach (var r in _ghostRenderers)
            if (r != null) r.SetPropertyBlock(_mpb);
    }

    private void DestroyGhost()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _ghostRenderers = null;
    }

    private static int GroundMask()
    {
        if (_groundMask == -1) _groundMask = LayerMask.GetMask("Ground");
        return _groundMask;
    }
}
