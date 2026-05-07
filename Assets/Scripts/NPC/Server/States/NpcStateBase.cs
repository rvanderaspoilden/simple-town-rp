using AI;

/// <summary>
/// Classe de base pour les états IA serveur d'un NPC.
/// Réutilise <see cref="AI.IState"/> / <see cref="AI.StateMachine"/> — exactement
/// le même pattern que PlayerController (CharacterIdle / CharacterMove / …).
///
/// Chaque état détient une référence vers le NpcAIController qui héberge la
/// state machine et expose les services nécessaires (NavMeshAgent, home,
/// despawn request, etc.).
/// </summary>
public abstract class NpcStateBase : IState {
    protected readonly NpcAIController Npc;

    protected NpcStateBase(NpcAIController npc) {
        Npc = npc;
    }

    /// <summary>
    /// Type logique de cet état (synchronisé au client). Doit être stable
    /// pour toute la durée de vie de l'instance.
    /// </summary>
    public abstract NpcStateType StateType { get; }

    public abstract void OnEnter();
    public abstract void Tick();
    public abstract void OnExit();
}
