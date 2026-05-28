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
        if (_actOpen == null) return acts;
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

    private void SendOpenRequest()
    {
        if (!NetworkClient.isConnected) {
            Debug.LogWarning("[StorageContainerBehaviour] OPEN ignoré : client non connecté.");
            return;
        }
        NetworkClient.Send(new C2S_OpenContainer { PropId = PropId });
    }
}
