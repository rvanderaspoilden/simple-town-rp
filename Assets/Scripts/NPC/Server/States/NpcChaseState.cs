using Sim;
using UnityEngine;

/// <summary>
/// Poursuite de la cible hostile : l'agent court (chaseSpeed de la config) vers la position
/// serveur de la cible, re-pathée à intervalle court. Les sorties (cible à portée d'attaque,
/// cible perdue) sont des transitions de la state machine.
/// </summary>
public class NpcChaseState : NpcStateBase {
    private const float RepathInterval = 0.2f;

    private float _nextRepath;

    public override NpcStateType StateType => NpcStateType.Chasing;

    public NpcChaseState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        float speed = Npc.HostileConfig != null ? Npc.HostileConfig.ChaseSpeed : 4f;
        Npc.SetAgentSpeed(speed);
        _nextRepath = 0f;
    }

    public override void Tick() {
        PlayerController target = Npc.HostileTarget;
        if (target == null) return; // la transition vers Return fera le ménage

        if (Time.time < _nextRepath) return;
        _nextRepath = Time.time + RepathInterval;
        Npc.SetDestination(target.transform.position);
    }

    public override void OnExit() {
        Npc.StopAgent();
        Npc.ResetAgentSpeed();
    }
}
