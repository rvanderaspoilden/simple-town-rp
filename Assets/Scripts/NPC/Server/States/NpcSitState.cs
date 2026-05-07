using Sim;
using UnityEngine;

/// <summary>
/// Le NPC va s'asseoir sur un siège libre dans sa room :
///   1. Demande un siège libre via <see cref="SeatService.TryFindFreeSeatInRoom"/>
///   2. Marche jusqu'au transform d'approche
///   3. Réserve le slot via <see cref="SeatService.TryReserveSeat"/>
///      (la même API que la voie joueur — aucune duplication de logique)
///   4. Snap au transform du slot, désactive l'agent, déclenche l'animation SIT
///   5. Reste assis un délai random
///   6. Libère le slot, réactive l'agent, transition vers Idle
///
/// L'animation SIT est pilotée via PlayerAnimator.SetAction(SIT) — exactement
/// comme CharacterSit. Aucun code d'animation NPC-spécifique.
///
/// HasFinished est levé quand l'état est terminé (pour transition vers Idle).
/// </summary>
public class NpcSitState : NpcStateBase {
    private enum Phase { Searching, Approaching, Sitting, Done }

    private Phase   _phase;
    private int     _seatPropId = -1;
    private float   _sitTimer;
    private Vector3 _approachPos;
    private Vector3 _preSitPosition;
    private PlayerAnimator _animator;

    public bool HasFinished => _phase == Phase.Done;

    public NpcSitState(NpcAIController npc) : base(npc) {
        _animator = Npc.GetComponent<PlayerAnimator>();
    }

    public override void OnEnter() {
        _phase      = Phase.Searching;
        _seatPropId = -1;

        if (SeatService.TryFindFreeSeatInRoom(Npc.RoomId, out _seatPropId, out _approachPos)) {
            Npc.SetDestination(_approachPos);
            _phase = Phase.Approaching;
        }
        else {
            // Pas de siège disponible — fallback immédiat vers Idle.
            _phase = Phase.Done;
        }
    }

    public override void Tick() {
        switch (_phase) {
            case Phase.Approaching:
                if (Npc.HasReachedDestination()) {
                    if (SeatService.TryReserveSeat(Npc, Npc.RoomId, _seatPropId, out Transform slot)) {
                        // Snap au siège — exact même séquence que CharacterSit.OnEnter
                        // (disable agent → set transform → SetAction(SIT)).
                        _preSitPosition = Npc.transform.position;
                        Npc.StopAgent();
                        Npc.SetAgentEnabled(false);
                        Npc.transform.position = slot.position;
                        Npc.transform.rotation = slot.rotation;

                        if (_animator != null) {
                            _animator.SetAction(CharacterAnimatorAction.SIT);
                        }

                        _sitTimer = Random.Range(Npc.MinSitSeconds, Npc.MaxSitSeconds);
                        _phase = Phase.Sitting;
                    }
                    else {
                        // Le slot a été pris entre la recherche et l'arrivée.
                        _phase = Phase.Done;
                    }
                }
                break;

            case Phase.Sitting:
                _sitTimer -= Time.deltaTime;
                if (_sitTimer <= 0f) {
                    LeaveSeat();
                    _phase = Phase.Done;
                }
                break;
        }
    }

    public override void OnExit() {
        // Sécurité — si on sort de l'état pour une raison externe (despawn), libère le siège.
        if (_phase == Phase.Sitting) {
            LeaveSeat();
        }
        _phase = Phase.Done;
    }

    private void LeaveSeat() {
        if (_animator != null) {
            _animator.SetAction(CharacterAnimatorAction.NONE);
        }
        if (_seatPropId > 0) {
            SeatService.ReleaseSeat(Npc, Npc.RoomId, _seatPropId);
        }
        // Téléport au point d'avant l'assise (sur la navmesh) puis réactive l'agent
        // — même séquence que CharacterSit.OnExit côté joueur.
        Npc.transform.position = _preSitPosition;
        Npc.SetAgentEnabled(true);
    }
}
