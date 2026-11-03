using UnityEngine;

/// <summary>
/// Retour au camp du NPC hostile (cible perdue : morte ou sortie de zone) : il rentre à son
/// home en trottinant et se SOIGNE intégralement à l'arrivée (reset classique de garde de
/// camp — on ne peut pas l'épuiser en le kitant hors de sa zone). Contrairement à
/// NpcBackToHomeState (passants), il ne despawne PAS : il reprend sa patrouille, et peut
/// ré-aggro en chemin (transition Return → Chase).
/// </summary>
public class NpcReturnHomeState : NpcStateBase {
    public bool HasArrived { get; private set; }

    public override NpcStateType StateType => NpcStateType.BackToHome;

    public NpcReturnHomeState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        HasArrived = false;
        float speed = Npc.HostileConfig != null ? Npc.HostileConfig.ChaseSpeed * 0.75f : 3f;
        Npc.SetAgentSpeed(speed);
        Npc.SetDestination(Npc.HomePosition);
    }

    public override void Tick() {
        if (HasArrived) return;
        if (Npc.HasReachedDestination()) {
            HasArrived = true;
            Npc.StopAgent();
            Npc.ServerHealFull();
        }
    }

    public override void OnExit() {
        HasArrived = false;
        Npc.ResetAgentSpeed();
    }
}
