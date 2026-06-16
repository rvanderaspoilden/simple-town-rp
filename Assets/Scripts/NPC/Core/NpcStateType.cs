/// <summary>
/// État logique d'un NPC, propagé du serveur au client.
///
/// Côté serveur c'est l'image directe de la state machine (NpcIdleState,
/// NpcGoToInterestAreaState, NpcBackToHomeState, NpcSitState).
/// Côté client c'est PUREMENT visuel : il pilote l'Animator et le mode de
/// rendu (interpolation vs snap) — aucune logique IA.
/// </summary>
public enum NpcStateType : byte {
    Idle                = 0,
    Walking             = 1,
    Sitting             = 2,
    GoingToInterestArea = 3,
    BackToHome          = 4,
    KnockedDown         = 5
}
