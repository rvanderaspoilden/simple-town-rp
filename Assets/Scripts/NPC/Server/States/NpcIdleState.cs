using UnityEngine;

/// <summary>
/// Le NPC reste immobile pendant un délai aléatoire, puis signale qu'il est prêt
/// à repartir (flag IsIdleComplete consommé par les transitions).
/// </summary>
public class NpcIdleState : NpcStateBase {
    private float _timer;

    public bool IsIdleComplete { get; private set; }

    public NpcIdleState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        IsIdleComplete = false;
        _timer = Random.Range(Npc.MinIdleSeconds, Npc.MaxIdleSeconds);
        Npc.StopAgent();
    }

    public override void Tick() {
        _timer -= Time.deltaTime;
        if (_timer <= 0f) {
            IsIdleComplete = true;
        }
    }

    public override void OnExit() {
        IsIdleComplete = false;
    }
}
