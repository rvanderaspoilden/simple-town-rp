/// <summary>
/// Famille de munitions : lie une arme (WeaponConfig.AmmoCategory) aux items munitions
/// qu'elle consomme au rechargement (AmmoConfig.AmmoCategory).
/// LIGHT = armes de poing (Glock), MEDIUM = fusils d'assaut (AK-47), HEAVY = sniper.
/// </summary>
public enum AmmoCategory {
    LIGHT,
    MEDIUM,
    HEAVY
}
