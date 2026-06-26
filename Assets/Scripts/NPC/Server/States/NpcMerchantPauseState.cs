using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Courte absence d'un NPC marchand : il s'éloigne du stand vers un point aléatoire proche
/// (dans <c>PauseWanderRadius</c>), patiente un court instant, puis signale qu'il a fini
/// (<see cref="HasFinished"/>) — la state machine le renvoie alors tenir son stand.
///
/// Pendant toute la pause l'état logique reste <see cref="NpcStateType.Idle"/> (dérivé en Walking
/// par le contrôleur quand l'agent se déplace) : jamais <see cref="NpcStateType.Merchant"/>, donc
/// l'action BUY est masquée côté client tant que le marchand est absent.
/// </summary>
public class NpcMerchantPauseState : NpcStateBase {
    private enum Phase { Moving, Waiting }

    private Phase _phase;
    private float _waitTimer;
    private bool  _finished;

    public override NpcStateType StateType => NpcStateType.Idle;

    public bool HasFinished => _finished;

    public NpcMerchantPauseState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        _finished  = false;
        _phase     = Phase.Moving;
        _waitTimer = Random.Range(Npc.Merchant.MinPauseSeconds, Npc.Merchant.MaxPauseSeconds);

        float radius = Npc.Merchant.PauseWanderRadius;
        Vector2 offset = Random.insideUnitCircle * radius;
        Vector3 candidate = Npc.HomePosition + new Vector3(offset.x, 0f, offset.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius + 1f, NavMesh.AllAreas))
            Npc.SetDestination(hit.position);
        else
            Npc.SetDestination(Npc.HomePosition); // repli : reste près du stand
    }

    public override void Tick() {
        if (_finished) return;

        if (_phase == Phase.Moving) {
            if (Npc.HasReachedDestination()) _phase = Phase.Waiting;
            return;
        }

        // Phase.Waiting
        _waitTimer -= Time.deltaTime;
        if (_waitTimer <= 0f) _finished = true;
    }

    public override void OnExit() {
        _finished = false;
    }
}
