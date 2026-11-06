using System.Collections.Generic;
using Interaction;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Étagère/portant de vêtements d'une boutique — objet de scène NON-NETWORK (pattern
/// <see cref="AtmBehaviour"/>), câblé sur les SM_Prop_Clothes_Rack_* de la City par
/// « Tools/The BROZ/Setup City Clothing Racks ». Une seule action « Essayer » (E court) qui
/// ouvre la cabine d'essayage (<see cref="Sim.UI.FittingRoomUI"/>) : caméra face au joueur,
/// choix des articles du <see cref="ClothingShopCatalogConfig"/> en preview sur le personnage,
/// puis achat du panier (revalidé côté serveur par <see cref="Sim.ClothingShopSystemBootstrap"/>,
/// qui vérifie la proximité via le registre statique All).
/// </summary>
public class ClothingRackBehaviour : MonoBehaviour, IInteractable, IQuickActionProvider {
    [Tooltip("Portée (m) d'interaction — distance planaire joueur↔portant.")]
    [SerializeField] private float interactionRange = 3f;

    [Tooltip("Catalogue des articles en vente. Vide = catalogue par défaut (Resources/Configurations/Shops/ClothingShopCatalog).")]
    [SerializeField] private ClothingShopCatalogConfig catalog;

    /// <summary>Registre des portants de la scène — consommé par la validation serveur de
    /// proximité des achats (le serveur charge aussi la City, pattern ATM).</summary>
    public static readonly List<ClothingRackBehaviour> All = new List<ClothingRackBehaviour>();

    private Action _tryOnAction;

    public ClothingShopCatalogConfig Catalog =>
        catalog != null ? catalog : ClothingShopCatalogConfig.LoadDefault();

    private void Awake() {
        _tryOnAction = Action.CreateRuntime(ActionTypeEnum.USE, "Essayer",
            Resources.Load<Action>("Configurations/Actions/USE")?.Icon);
        _tryOnAction.OnExecute += OnTryOnExecuted;
    }

    private void OnEnable()  => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void OnDestroy() {
        if (_tryOnAction != null) _tryOnAction.OnExecute -= OnTryOnExecuted;
    }

    // ── IInteractable ───────────────────────────────────────────────────────────────
    public float GetRange()         => interactionRange;
    public bool  IsRightClickOnly() => false;
    public void  StopInteraction()  { }
    public bool  IsInteractable()   => _tryOnAction != null && Catalog != null;

    public Action[] GetActions(bool withPriority = false) =>
        _tryOnAction != null ? new[] { _tryOnAction } : System.Array.Empty<Action>();

    // Une seule action → E court l'exécute directement (prompt « Essayer »).
    public bool TryGetQuickAction(Action[] available, out Action action) {
        action = _tryOnAction;
        return _tryOnAction != null;
    }

    private void OnTryOnExecuted(Action _) {
        Sim.UI.FittingRoomUI.Instance?.Open(this);
    }

    /// <summary>Portant le plus proche à portée (planaire) de cette position, ou null. Sert à
    /// la validation serveur des achats (portée client + tolérance réseau) — le catalogue
    /// facturé est CELUI du portant résolu.</summary>
    public static ClothingRackBehaviour NearestInRange(Vector3 position, float range) {
        ClothingRackBehaviour best = null;
        float bestSqr = range * range;
        foreach (ClothingRackBehaviour rack in All) {
            if (rack == null) continue;
            Vector3 d = rack.transform.position - position;
            d.y = 0f;
            if (d.sqrMagnitude <= bestSqr) {
                bestSqr = d.sqrMagnitude;
                best = rack;
            }
        }
        return best;
    }
}
