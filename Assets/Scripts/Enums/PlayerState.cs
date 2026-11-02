public enum PlayerState : byte {
    IDLE,
    DIED,
    INTERACTING,
    SLEEPING,
    LOOKING,
    SITTING,
    MOVING,

    // Engagé dans une action à UI bloquante (ATM, fouille de cadavre / sac de butin).
    // Posé par l'état CharacterBusy ; neutralise hotbar / tir / mêlée le temps de l'action.
    BUSY
}