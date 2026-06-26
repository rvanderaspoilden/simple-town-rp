using UnityEngine;

/// <summary>
/// État « tient le stand » d'un NPC marchand. Le NPC revient à son stand (le transform de son
/// <see cref="NpcSpawnPoint"/>) puis s'y tient immobile, face au stand, pendant une période
/// aléatoire. Durant cette tenue il est en <see cref="NpcStateType.Merchant"/> → interactable
/// côté client (action BUY).
///
/// À la fin d'une période, un tirage unique décide soit de prolonger la tenue (rechargement du
/// timer, pas de self-transition — cf. <see cref="AI.StateMachine.SetState"/> qui ignore from==to),
/// soit de partir en courte pause (<see cref="NpcMerchantPauseState"/>) pour rester vivant.
/// </summary>
public class NpcMerchantState : NpcStateBase {
    private enum Phase { Returning, Tending }

    private Phase _phase;
    private float _tendTimer;
    private bool  _pauseRequested;

    public override NpcStateType StateType =>
        _phase == Phase.Tending ? NpcStateType.Merchant : NpcStateType.Idle;

    /// <summary>Vrai quand une période de tenue s'achève ET que le tirage décide d'une pause.
    /// Consommé par la transition vers <see cref="NpcMerchantPauseState"/>.</summary>
    public bool WantsPause => _pauseRequested;

    public NpcMerchantState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        _pauseRequested = false;
        _phase          = Phase.Returning;
        // Revient au stand. Si déjà sur place, HasReachedDestination passera vrai dès le prochain
        // Tick et on enchaîne sur la tenue.
        Npc.SetDestination(Npc.HomePosition);
    }

    public override void Tick() {
        if (_phase == Phase.Returning) {
            if (Npc.HasReachedDestination()) EnterTending();
            return;
        }

        // Phase.Tending
        _tendTimer -= Time.deltaTime;
        if (_tendTimer > 0f || _pauseRequested) return;

        // Fin d'une période : un seul tirage décide pause vs prolongation.
        if (Random.value < Npc.Merchant.PauseProbability) {
            _pauseRequested = true; // la transition vers la pause se déclenchera au prochain Tick
        }
        else {
            _tendTimer = Random.Range(Npc.Merchant.MinTendSeconds, Npc.Merchant.MaxTendSeconds);
        }
    }

    public override void OnExit() {
        _pauseRequested = false;
    }

    private void EnterTending() {
        _phase = Phase.Tending;
        Npc.StopAgent();
        Npc.FaceHome();
        _tendTimer = Random.Range(Npc.Merchant.MinTendSeconds, Npc.Merchant.MaxTendSeconds);
    }
}
