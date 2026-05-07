using UnityEngine;

/// <summary>
/// Choisit un point d'intérêt aléatoire (différent du dernier visité) et s'y rend
/// via NavMeshAgent. Lève HasArrived une fois le chemin terminé.
/// Si aucun point d'intérêt n'est disponible, lève HasArrived immédiatement
/// (la transition vers Idle est alors instantanée).
/// </summary>
public class NpcGoToInterestAreaState : NpcStateBase {
    private InterestPoint _destination;

    public bool HasArrived { get; private set; }

    public override NpcStateType StateType => NpcStateType.GoingToInterestArea;

    public NpcGoToInterestAreaState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        HasArrived = false;
        _destination = InterestPointRegistry.Instance.PickRandom(exclude: Npc.LastVisitedInterest);

        if (_destination == null) {
            // Aucun point disponible — on retombera en Idle immédiatement.
            HasArrived = true;
            return;
        }

        Npc.SetDestination(_destination.Position);
        Npc.LastVisitedInterest = _destination;
    }

    public override void Tick() {
        if (HasArrived) return;
        if (Npc.HasReachedDestination()) {
            HasArrived = true;
            Npc.IncrementVisitCount();
        }
    }

    public override void OnExit() {
        HasArrived = false;
        _destination = null;
    }
}
