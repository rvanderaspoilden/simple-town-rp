/// <summary>
/// État logique d'un NPC, propagé du serveur au client.
///
/// Côté serveur c'est l'image directe de la state machine (NpcIdleState,
/// NpcGoToInterestAreaState, NpcBackToHomeState, NpcSitState, NpcMerchantState).
/// Côté client c'est PUREMENT visuel : il pilote l'Animator et le mode de
/// rendu (interpolation vs snap) — aucune logique IA.
///
/// Merchant = le NPC tient son stand (immobile, interactable côté client → action
/// BUY). Pendant ses pauses il repasse en Idle/Walking (donc BUY masqué). Aucune
/// animation dédiée : Merchant retombe sur l'idle debout par défaut.
///
/// Interacting = overlay transverse (pas un état de la state machine) : le NPC est
/// figé par un ou plusieurs joueurs qui interagissent. Côté client : idle visuel
/// (vélocité 0). Cf. NpcInteractionService / NpcAIController.ServerBeginInteraction.
/// </summary>
public enum NpcStateType : byte {
    Idle                = 0,
    Walking             = 1,
    Sitting             = 2,
    GoingToInterestArea = 3,
    BackToHome          = 4,
    KnockedDown         = 5,
    Merchant            = 6,
    Interacting         = 7,
    /// <summary>PV à 0 : cadavre ragdoll au sol en attente de despawn. Même visuel client
    /// que KnockedDown, mais la vue devient « fouillable » (action SEARCH → loot).</summary>
    Dead                = 8,

    // ── États HOSTILES (NPC agressifs, cf. HostileNpcConfig) ──────────────────
    /// <summary>Patrouille autour du home (hostile sans cible). Côté client : anim pilotée
    /// par la vélocité, comme Idle/Walking.</summary>
    Patrolling          = 9,
    /// <summary>Poursuite d'un joueur (course). Côté client : anim vélocité (Run) + icône
    /// d'aggro teintée rouge.</summary>
    Chasing             = 10,
    /// <summary>À portée d'attaque : immobile face à la cible, coups diffusés en one-shots
    /// (S2C_NpcAttack). Côté client : pose de visée tenue si arme à feu.</summary>
    Attacking           = 11
}
