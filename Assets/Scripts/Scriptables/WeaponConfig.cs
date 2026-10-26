using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon", menuName = "Configurations/Item/Weapon")]
public class WeaponConfig : ItemConfig {
    [Header("Weapon")]
    [Tooltip("Délai minimal (s) entre deux tirs — appliqué côté client ET revalidé serveur.")]
    [SerializeField]
    private float fireInterval = 0.35f;

    [Tooltip("Tir automatique : clic gauche MAINTENU = rafale à la cadence fireInterval (ex. AK-47). Décoché = un tir par clic (ex. Glock).")]
    [SerializeField]
    private bool automatic;

    [Tooltip("Portée maximale (m) du tir : raycast serveur et point de visée client.")]
    [SerializeField]
    private float maxRange = 40f;

    [Tooltip("Intensité du kick de recul par tir (1 = pleine impulsion). Baisser pour les automatiques, sinon le canon grimpe au plafond en rafale.")]
    [Range(0.05f, 1f)]
    [SerializeField]
    private float recoilKick = 1f;

    [Tooltip("Dispersion des balles (demi-angle du cône, deg) en tir SANS visée (hip fire). En visée elle est multipliée par aimSpreadFactor du PlayerShooter (fortement réduite).")]
    [Range(0f, 15f)]
    [SerializeField]
    private float spreadAngle = 4f;

    [Header("Lunette (visée FPS sniper)")]
    [Tooltip("Visée à la lunette : pendant la visée (clic droit) la caméra passe en vue FPS à l'œil du personnage avec un zoom fort (scopeFov) et une sensibilité réduite (scopeSensitivityFactor). Le corps du joueur local est masqué (ombre conservée). La dispersion est NULLE à la lunette.")]
    [SerializeField]
    private bool scopedAim;

    [Range(1f, 40f)]
    [Tooltip("FOV (deg) de la lunette — plus petit = zoom plus fort (8 ≈ x5 depuis le FOV 40 de la vcam TPS).")]
    [SerializeField]
    private float scopeFov = 8f;

    [Range(0.01f, 1f)]
    [Tooltip("Facteur de sensibilité souris pendant la visée à la lunette (remplace le aimSensitivityFactor de la caméra TPS, qui est pensé pour un zoom léger).")]
    [SerializeField]
    private float scopeSensitivityFactor = 0.1f;

    [Tooltip("Son du coup de feu, joué en 3D à la bouche du canon sur tous les clients.")]
    [SerializeField]
    private AudioClip fireSound;

    [Range(0f, 1f)]
    [SerializeField]
    private float fireSoundVolume = 0.8f;

    public float FireInterval => Mathf.Max(0.05f, fireInterval);

    public bool Automatic => automatic;

    public float MaxRange => Mathf.Max(1f, maxRange);

    public float RecoilKick => Mathf.Clamp(recoilKick, 0.05f, 1f);

    public float SpreadAngle => Mathf.Clamp(spreadAngle, 0f, 15f);

    public bool ScopedAim => scopedAim;

    public float ScopeFov => Mathf.Clamp(scopeFov, 1f, 40f);

    public float ScopeSensitivityFactor => Mathf.Clamp(scopeSensitivityFactor, 0.01f, 1f);

    public AudioClip FireSound => fireSound;

    public float FireSoundVolume => fireSoundVolume;
}
