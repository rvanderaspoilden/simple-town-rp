using System.Collections.Generic;
using Interaction;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Distributeur automatique (ATM) — objet de scène NON-NETWORK (pattern <see cref="ForSaleSign"/>)
/// posé devant les murs SM_Bld_Wall_ATM_01 de la City par l'outil
/// « Tools/The BROZ/Setup City ATMs ». Une seule action « Utiliser » (action rapide sur E court)
/// qui ouvre <see cref="Sim.UI.AtmUI"/> (retrait/dépôt). Pendant la session, la CinemachineCamera
/// enfant (priorité supérieure à la vcam TPS) amène la vue devant l'écran — le brain fait le
/// blend aller-retour. Retrait/dépôt exécutés et revalidés côté serveur par
/// <see cref="Sim.AtmSystemBootstrap"/>, qui vérifie la proximité via le registre statique All.
/// </summary>
public class AtmBehaviour : MonoBehaviour, IInteractable, IQuickActionProvider {
    [Tooltip("Portée (m) d'interaction — distance planaire joueur↔ATM (CanInteractWith).")]
    [SerializeField] private float interactionRange = 3f;

    [Tooltip("GameObject (désactivé) portant la CinemachineCamera placée devant l'écran. Activé pendant la session ATM — le brain blend la vue dessus.")]
    [SerializeField] private GameObject viewCamera;

    [Tooltip("Ancre du canvas world-space de l'écran ATM : posé juste devant la dalle, face au joueur. L'AtmUI (prefab AtmScreenUI) s'y aligne à l'ouverture.")]
    [SerializeField] private Transform screenAnchor;

    [Tooltip("Collider de la fente à carte : cliquer dessus pendant l'état « Insérez votre carte » simule l'insertion de la CB (AtmUI).")]
    [SerializeField] private Collider cardSlot;

    [Tooltip("Visuel de la CB à moitié insérée dans la fente (désactivé hors session). AtmUI l'anime : outline clignotant pendant « Insérez votre carte », glissement dans la fente au clic, éjection au « Rendre la carte ».")]
    [SerializeField] private Transform cardVisual;

    [Tooltip("Visuel de la liasse dans la fente à billets du bas (désactivé hors session). AtmUI l'anime : sort de la fente au retrait, avalée au dépôt.")]
    [SerializeField] private Transform cashVisual;

    /// <summary>Registre des ATMs de la scène — consommé par la validation serveur de
    /// proximité et l'auto-fermeture d'AtmUI, sans FindObjectsByType par requête.</summary>
    public static readonly List<AtmBehaviour> All = new List<AtmBehaviour>();

    private Action _useAction;

    private void Awake() {
        _useAction = Action.CreateRuntime(ActionTypeEnum.USE, "Utiliser",
            Resources.Load<Action>("Configurations/Actions/USE")?.Icon);
        _useAction.OnExecute += OnUseExecuted;
    }

    private void OnEnable()  => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void OnDestroy() {
        if (_useAction != null) _useAction.OnExecute -= OnUseExecuted;
    }

    // ── IInteractable ───────────────────────────────────────────────────────────────
    public float GetRange()         => interactionRange;
    public bool  IsRightClickOnly() => false;
    public void  StopInteraction()  { }
    public bool  IsInteractable()   => _useAction != null;

    public Action[] GetActions(bool withPriority = false) =>
        _useAction != null ? new[] { _useAction } : System.Array.Empty<Action>();

    // Une seule action → E court l'exécute directement (prompt « Utiliser »).
    public bool TryGetQuickAction(Action[] available, out Action action) {
        action = _useAction;
        return _useAction != null;
    }

    /// <summary>Ancre du canvas écran (repli : le transform de l'ATM lui-même).</summary>
    public Transform ScreenAnchor => screenAnchor != null ? screenAnchor : transform;

    /// <summary>Collider de la fente à carte (null si non câblé par l'outil de setup).</summary>
    public Collider CardSlot => cardSlot;

    /// <summary>Visuel de la CB dans la fente (null si non câblé par l'outil de setup).</summary>
    public Transform CardVisual => cardVisual;

    /// <summary>Visuel de la liasse dans la fente à billets (null si non câblé par l'outil de setup).</summary>
    public Transform CashVisual => cashVisual;

    private void OnUseExecuted(Action _) {
        Sim.UI.AtmUI.GetOrCreate()?.Open(this);
    }

    /// <summary>Active/coupe la vue écran de la session ATM (vcam prioritaire).</summary>
    public void SetViewActive(bool active) {
        if (viewCamera != null) viewCamera.SetActive(active);
    }

    /// <summary>Un ATM se trouve-t-il à portée (planaire) de cette position ? Utilisé par la
    /// validation serveur des retraits/dépôts (portée client + tolérance réseau).</summary>
    public static bool AnyInRange(Vector3 position, float range) {
        foreach (AtmBehaviour atm in All) {
            if (atm == null) continue;
            Vector3 d = atm.transform.position - position;
            d.y = 0f;
            if (d.sqrMagnitude <= range * range) return true;
        }
        return false;
    }
}
