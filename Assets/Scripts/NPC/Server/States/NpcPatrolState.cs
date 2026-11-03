using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Patrouille du NPC hostile : alternance marche ↔ pause autour de son home, dans le rayon
/// de patrouille de la HostileNpcConfig. Les points sont tirés au hasard autour du home et
/// projetés sur la NavMesh. La sortie vers Chase est une transition de la state machine
/// (HasHostileTarget), pas une responsabilité de cet état.
/// </summary>
public class NpcPatrolState : NpcStateBase {
    private const float MinPauseSeconds = 1.5f;
    private const float MaxPauseSeconds = 4f;
    private const float NavSampleRadius = 2f;
    private const int   MaxPickAttempts = 4;

    private float _pauseTimer;
    private bool  _moving;

    public override NpcStateType StateType => NpcStateType.Patrolling;

    public NpcPatrolState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        Npc.ResetAgentSpeed();
        Npc.StopAgent();
        _moving = false;
        _pauseTimer = Random.Range(MinPauseSeconds, MaxPauseSeconds);
    }

    public override void Tick() {
        if (_moving) {
            if (Npc.HasReachedDestination()) {
                _moving = false;
                _pauseTimer = Random.Range(MinPauseSeconds, MaxPauseSeconds);
            }
            return;
        }

        _pauseTimer -= Time.deltaTime;
        if (_pauseTimer > 0f) return;

        if (TryPickPatrolPoint(out Vector3 point)) {
            Npc.SetDestination(point);
            _moving = true;
        }
        else {
            _pauseTimer = Random.Range(MinPauseSeconds, MaxPauseSeconds);
        }
    }

    public override void OnExit() {
        _moving = false;
    }

    /// <summary>Point de patrouille aléatoire autour du home, projeté sur la NavMesh.</summary>
    private bool TryPickPatrolPoint(out Vector3 point) {
        float radius = Npc.HostileConfig != null ? Npc.HostileConfig.PatrolRadius : 8f;
        Vector3 home = Npc.HomePosition;

        for (int i = 0; i < MaxPickAttempts; i++) {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = home + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas)) {
                point = hit.position;
                return true;
            }
        }

        point = default;
        return false;
    }
}
