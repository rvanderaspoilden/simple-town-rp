using UnityEngine;

/// <summary>
/// Le NPC retourne à son point de spawn (home). Une fois arrivé, demande au
/// SpawnManager de le despawn proprement (qui programmera un futur respawn).
/// </summary>
public class NpcBackToHomeState : NpcStateBase {
    private bool _despawnRequested;

    public NpcBackToHomeState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        _despawnRequested = false;
        Npc.SetDestination(Npc.HomePosition);
    }

    public override void Tick() {
        if (_despawnRequested) return;
        if (Npc.HasReachedDestination()) {
            _despawnRequested = true;
            NpcSpawnManager.Instance.RequestDespawn(Npc);
        }
    }

    public override void OnExit() {
        _despawnRequested = false;
    }
}
