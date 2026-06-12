using System.Collections.Generic;
using Mirror;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Base d'un conteneur de stockage (frigo, placard…). L'action OPEN ouvre la HUD
/// grille côté client. Le serveur ne reconnaît un prop comme conteneur que si
/// <c>PropsConfig.Container.IsContainer</c> = true.
///
/// L'action OPEN est INJECTÉE dynamiquement (pas attendue dans PropsConfig.actions),
/// même pattern que BUILD/MOVE/DESTROY génériques. Au clic OPEN, on émet
/// <see cref="C2S_OpenContainer"/> ; le serveur répond avec
/// <see cref="S2C_ContainerOpened"/> qui pilote l'UI grille.
/// </summary>
public class StorageContainerBehaviour : PropBehaviourBase
{
    // Action OPEN partagée — chargée une fois, instanciée par prop pour ne pas
    // partager les delegates OnExecute.
    private Action _actOpen;
    private static Action _protoOpen;
    private static bool _protoLoaded;

    [Header("Visual")]
    [Tooltip("Animateur de porte (optionnel) ; piloté par S2C_ContainerVisualState.")]
    [SerializeField] private ContainerDoorAnimator doorAnimator;

    protected override void Awake()
    {
        base.Awake();
        bool isContainer = configuration != null
                           && configuration.Container != null
                           && configuration.Container.IsContainer;
        if (isContainer) {
            LoadProto();
            _actOpen = InstantiateAction(_protoOpen);
        }
    }

    protected override void OnDestroy()
    {
        if (_actOpen != null) {
            // OnExecute delegate géré par PropBehaviourBase.UnsubscribeActions ; on
            // s'aligne au pattern des sale/generic actions injectées : pas de manip ici.
        }
        base.OnDestroy();
    }

    private static void LoadProto()
    {
        if (_protoLoaded) return;
        _protoOpen = Resources.Load<Action>("Configurations/Actions/OPEN");
        _protoLoaded = true;
        if (_protoOpen == null)
            Debug.LogWarning("[StorageContainerBehaviour] OPEN action introuvable à Resources/Configurations/Actions/OPEN");
    }

    public override Action[] GetActions(bool withPriority = false)
    {
        Action[] acts = base.GetActions(withPriority);
        // Sur un prop d'expo (ShopDisplay), la base court-circuite déjà à [BUY] :
        // on ne doit RIEN ajouter, sinon le radial affiche BUY + OPEN. Le contenu
        // d'un présentoir n'est pas accessible — c'est un stock infini visuel.
        // OPEN n'a de sens qu'une fois le meuble CONSTRUIT (pas en état à-construire).
        if (_actOpen == null || IsShopDisplay || !IsBuilt()) return acts;
        // Évite de dupliquer si elle se retrouvait déjà dans la liste.
        for (int i = 0; i < acts.Length; i++) {
            if (acts[i] == _actOpen) return acts;
        }
        var list = new List<Action>(acts.Length + 1);
        list.AddRange(acts);
        list.Add(_actOpen);
        return list.ToArray();
    }

    protected override void Execute(Action action)
    {
        if (action.Type == ActionTypeEnum.OPEN) {
            SendOpenRequest();
            return;
        }
        base.Execute(action);
    }

    /// <summary>
    /// Émis sur le client opener au moment du clic OPEN, AVANT l'aller-retour serveur.
    /// Permet à <see cref="ContainerPanelUI"/> d'ouvrir le panneau optimistement (perçu instantané)
    /// pendant que le serveur fait son POST+GET vers le backend (~50-300ms).
    /// Args : propId, slotCount, displayName (titre du panneau, fallback "Conteneur" si vide).
    /// </summary>
    public static event System.Action<int, int, string> OnOpenRequested;

    private void SendOpenRequest()
    {
        if (!NetworkClient.isConnected) {
            Debug.LogWarning("[StorageContainerBehaviour] OPEN ignoré : client non connecté.");
            return;
        }
        int slotCount = configuration?.Container != null ? configuration.Container.SlotCount : 0;
        string displayName = configuration != null ? configuration.GetDisplayName() : null;
        if (slotCount > 0) OnOpenRequested?.Invoke(PropId, slotCount, displayName);
        NetworkClient.Send(new C2S_OpenContainer { PropId = PropId });
    }

    /// <summary>
    /// Appelé par ClientPropManager sur réception de S2C_ContainerVisualState.
    /// No-op si aucun animateur n'est posé sur ce prefab (cas d'un conteneur sans porte mobile).
    /// </summary>
    public void SetOpenState(bool open) {
        if (doorAnimator != null) doorAnimator.SetOpen(open);
    }
}
