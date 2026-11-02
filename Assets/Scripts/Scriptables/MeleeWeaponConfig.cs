using UnityEngine;

[CreateAssetMenu(fileName = "New Melee Weapon", menuName = "Configurations/Item/Melee Weapon")]
public class MeleeWeaponConfig : ItemConfig {
    [Header("Melee Weapon")]
    [Tooltip("Délai minimal (s) entre deux coups — appliqué côté client ET revalidé serveur.")]
    [SerializeField]
    private float attackInterval = 0.7f;

    [Tooltip("Dégâts (PV) infligés au joueur touché par un coup. Le joueur meurt quand ses PV atteignent 0.")]
    [Min(0f)]
    [SerializeField]
    private float damage = 20f;

    [Tooltip("Portée maximale (m) du coup, mesurée du corps de l'attaquant à celui de la cible (positions serveur).")]
    [SerializeField]
    private float maxRange = 2.2f;

    [Tooltip("Demi-angle (deg) du cône frappé devant l'attaquant : une cible hors cône n'est pas touchée même à portée.")]
    [Range(10f, 90f)]
    [SerializeField]
    private float hitAngle = 55f;

    [Tooltip("Un coup qui touche RENVERSE la cible (knockdown ragdoll), comme un tir d'arme à feu.")]
    [SerializeField]
    private bool knockdown = true;

    [Tooltip("Son du swing (whoosh), joué en 3D sur l'attaquant sur tous les clients au départ du coup.")]
    [SerializeField]
    private AudioClip swingSound;

    [Range(0f, 1f)]
    [SerializeField]
    private float swingSoundVolume = 0.7f;

    [Tooltip("Son d'impact, joué en 3D au point touché sur tous les clients quand le coup porte.")]
    [SerializeField]
    private AudioClip hitSound;

    [Range(0f, 1f)]
    [SerializeField]
    private float hitSoundVolume = 0.8f;

    public float AttackInterval => Mathf.Max(0.2f, attackInterval);

    public float Damage => Mathf.Max(0f, damage);

    public float MaxRange => Mathf.Max(0.5f, maxRange);

    public float HitAngle => Mathf.Clamp(hitAngle, 10f, 90f);

    public bool Knockdown => knockdown;

    public AudioClip SwingSound => swingSound;

    public float SwingSoundVolume => swingSoundVolume;

    public AudioClip HitSound => hitSound;

    public float HitSoundVolume => hitSoundVolume;
}
