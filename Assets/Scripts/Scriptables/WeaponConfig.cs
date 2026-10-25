using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon", menuName = "Configurations/Item/Weapon")]
public class WeaponConfig : ItemConfig {
    [Header("Weapon")]
    [Tooltip("Délai minimal (s) entre deux tirs — appliqué côté client ET revalidé serveur.")]
    [SerializeField]
    private float fireInterval = 0.35f;

    [Tooltip("Portée maximale (m) du tir : raycast serveur et point de visée client.")]
    [SerializeField]
    private float maxRange = 40f;

    [Tooltip("Son du coup de feu, joué en 3D à la bouche du canon sur tous les clients.")]
    [SerializeField]
    private AudioClip fireSound;

    [Range(0f, 1f)]
    [SerializeField]
    private float fireSoundVolume = 0.8f;

    public float FireInterval => Mathf.Max(0.05f, fireInterval);

    public float MaxRange => Mathf.Max(1f, maxRange);

    public AudioClip FireSound => fireSound;

    public float FireSoundVolume => fireSoundVolume;
}
