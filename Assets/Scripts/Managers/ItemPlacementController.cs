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
///
/// Cycle :
///   Idle → Begin() → Positioning (ghost + BuildPreview, BUILD camera + HUD « Mode Pose »,
///     validation = clic-gauche / Entrée, uniquement si BuildPreview.IsPlaceable()) →
///   envoi C2S_PoseHeldItem (l'item se détache et apparaît à l'emplacement) → retour TPS → Idle.
///   (Full TPS : plus de marche automatique vers le point de pose — l'item se pose directement.)
///
/// Annulation pendant Positioning : clic-droit / Échap → ghost détruit, retour TPS, item gardé.
///
/// Le BuildPreview gère lui-même la détection de collision (l'item ne traverse plus les objets)
/// et la surface posable (sol / Posable Surface). En placement « libre » (item, pas de
/// PropBehaviourBase) il n'exige aucune Buildable Area : validité = pas de collision + sol dessous.
///
/// Singleton paresseux DontDestroyOnLoad (créé par code, aucun prefab à éditer).
/// </summary>
public class ItemPlacementController : MonoBehaviour
{
    private enum Phase { Idle, Positioning }

    // ── Tuning ────────────────────────────────────────────────────────────────
    private const float CursorRayLength   = 200f;

    private Phase    _phase = Phase.Idle;
    private ItemBehaviour _item;
    private HandType _hand;

    private GameObject _ghost;
    private BuildPreview _preview;

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
        if (_phase != Phase.Idle)
            Abort();

        _item = item;
        _hand = item.HolderHand;

        HUDManager.Instance?.CloseInventory();

        SpawnGhost(item.Configuration.ID, player.transform.position);

        player.SetState(StateType.PLACING_ITEM);          // → caméra BUILD (curseur libre)
        HUDManager.Instance?.DisplayPanel(PanelTypeEnum.POSE); // → HUD dédié « Mode Pose » (image only)
        _phase = Phase.Positioning;
    }

    // ── Boucle ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_phase == Phase.Positioning) TickPositioning();
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

        // Confirmation sur le RELÂCHEMENT du clic gauche.
        if (Input.GetMouseButtonUp(0) && IsPlaceable()) Confirm();
    }

    private bool IsPlaceable() => _preview != null && _preview.IsPlaceable();

    /// <summary>Emplacement validé : on demande directement au serveur de détacher et poser
    /// l'item (full TPS — plus de marche automatique), puis retour caméra TPS.</summary>
    private void Confirm()
    {
        Vector3    pos  = _ghost.transform.position;
        Quaternion rot  = _ghost.transform.rotation;
        HandType   hand = _hand;

        DestroyGhost();
        HUDManager.Instance?.DisplayPanel(PanelTypeEnum.DEFAULT);
        PlayerController.Local?.SetState(StateType.FREE); // retour caméra TPS

        NetworkClient.Send(new C2S_PoseHeldItem
        {
            Hand     = hand,
            Position = pos,
            Rotation = rot
        });
        ResetState();
    }

    // ── Annulation / cleanup ────────────────────────────────────────────────────

    /// <summary>Annulation pendant le positionnement : ghost détruit, retour TPS, item gardé.</summary>
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
