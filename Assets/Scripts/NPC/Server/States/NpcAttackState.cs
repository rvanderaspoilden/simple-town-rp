using Sim;
using Sim.NPC;
using UnityEngine;

/// <summary>
/// Attaque de la cible hostile, à l'arrêt face à elle :
///   - MÊLÉE / POINGS : diffuse le swing (S2C_NpcAttack) puis résout l'impact en différé
///     (~0.3 s, calé sur la descente de l'anim procédurale — même timing que PlayerMelee) ;
///   - ARME À FEU : tir immédiat (NpcCombat.FireRanged) si la ligne de vue est dégagée.
/// La cadence vient de l'arme (× attackCadenceFactor), le premier coup attend reactionDelay.
/// Les sorties (cible hors de portée → Chase, cible perdue → Return) sont des transitions.
/// </summary>
public class NpcAttackState : NpcStateBase {
    // Timing d'impact mêlée, aligné sur l'anim procédurale (cf. PlayerMelee / NpcAttackAnimator).
    private const float WeaponStrikeDelay = 0.33f;
    private const float PunchStrikeDelay  = 0.18f;
    private const float FaceTurnSpeedDeg  = 540f;

    private float _nextAttackAt;
    private float _pendingImpactAt = -1f;

    public override NpcStateType StateType => NpcStateType.Attacking;

    public NpcAttackState(NpcAIController npc) : base(npc) { }

    public override void OnEnter() {
        Npc.StopAgent();
        float reaction = Npc.HostileConfig != null ? Npc.HostileConfig.ReactionDelay : 0.5f;
        _nextAttackAt = Time.time + reaction;
        _pendingImpactAt = -1f;
    }

    public override void Tick() {
        HostileNpcConfig cfg = Npc.HostileConfig;
        PlayerController target = Npc.HostileTarget;
        if (cfg == null || target == null) return;

        Npc.FaceTowards(target.transform.position, FaceTurnSpeedDeg);

        // Impact de mêlée différé (le swing a déjà été diffusé).
        if (_pendingImpactAt > 0f && Time.time >= _pendingImpactAt) {
            _pendingImpactAt = -1f;
            NpcCombat.ResolveMeleeImpact(Npc, target, cfg.AttackDamage, cfg.MeleeRange, cfg.MeleeHitAngle);
        }

        if (Time.time < _nextAttackAt) return;
        if (!Npc.TargetInAttackReach()) return; // la transition vers Chase reprendra la poursuite

        if (cfg.IsRanged) {
            NpcCombat.FireRanged(Npc, target, cfg.RangedWeapon, cfg.RangedSpreadBonus);
            _nextAttackAt = Time.time + cfg.AttackInterval;
            return;
        }

        // Mêlée : swing d'arme, ou poings alternés droite/gauche.
        bool punch = cfg.Weapon == null;
        NpcAttackKind kind = !punch ? NpcAttackKind.MeleeSwing
            : Npc.NextPunchLeft() ? NpcAttackKind.PunchLeft : NpcAttackKind.PunchRight;

        NpcServerManager.Instance.Attack(Npc.NpcId, kind, Vector3.zero);
        _pendingImpactAt = Time.time + (punch ? PunchStrikeDelay : WeaponStrikeDelay);
        _nextAttackAt = Time.time + cfg.AttackInterval;
    }

    public override void OnExit() {
        _pendingImpactAt = -1f;
    }
}
