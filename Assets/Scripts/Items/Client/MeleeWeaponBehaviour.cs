using Sim.Audio;
using UnityEngine;

/// <summary>
/// Item « arme de mêlée » (ex. hache). Étend ItemBehaviour avec les effets de coup purement
/// visuels/sonores : whoosh du swing et son d'impact. La logique d'attaque (input, validation
/// serveur, dégâts, knockdown) vit dans <see cref="Sim.PlayerMelee"/> — ce composant tourne
/// sur toutes les copies et ne fait que du feedback.
/// </summary>
public class MeleeWeaponBehaviour : ItemBehaviour {
    public MeleeWeaponConfig MeleeConfiguration => Configuration as MeleeWeaponConfig;

    /// <summary>Joue le whoosh du swing sur CE client, en 3D sur l'arme. Appelé au départ du
    /// coup : localement par l'owner (zéro latence), via PlayerMelee.RpcSwing ailleurs.</summary>
    public void PlaySwingEffects() {
        MeleeWeaponConfig cfg = MeleeConfiguration;
        if (cfg != null && cfg.SwingSound != null && AudioManager.Instance != null) {
            AudioManager.Instance.PlayClip3D(cfg.SwingSound, transform.position, cfg.SwingSoundVolume);
        }
    }

    /// <summary>Joue le son d'impact sur CE client au point touché. Appelé par
    /// PlayerMelee.RpcHit sur toutes les copies quand le coup porte.</summary>
    public void PlayHitEffects(Vector3 hitPoint) {
        MeleeWeaponConfig cfg = MeleeConfiguration;
        if (cfg != null && cfg.HitSound != null && AudioManager.Instance != null) {
            AudioManager.Instance.PlayClip3D(cfg.HitSound, hitPoint, cfg.HitSoundVolume);
        }
    }
}
