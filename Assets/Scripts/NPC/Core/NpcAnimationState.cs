/// <summary>
/// État d'animation simplifié transmis par le serveur.
/// Le client mappe ces valeurs sur son propre Animator local.
/// </summary>
public enum NpcAnimationState : byte {
    Idle = 0,
    Walk = 1
}
