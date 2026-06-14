using UnityEngine;

/// <summary>
/// Configuration data-driven d'un véhicule : identité (modèle), capacité (passagers, coffre),
/// paramètres de conduite (vitesses, accélération, freinage, braquage) et sons assignés.
///
/// Source unique de vérité référencée par <see cref="VehicleController"/> : changer un modèle,
/// sa vitesse ou ses sons se fait dans l'asset, sans toucher au code. Un véhicule sans config
/// retombe sur les valeurs par défaut sérialisées du contrôleur.
///
/// Namespace global (cohérent avec ItemConfig et les types réseau Mirror).
/// </summary>
[CreateAssetMenu(fileName = "New Vehicle Config", menuName = "Configurations/Vehicle")]
public class VehicleConfig : ScriptableObject {
    [Header("Identity")]
    [Tooltip("Identifiant stable du véhicule (clé de résolution config↔DB↔prefab). Unique, ex. « city-car ».")]
    public string id = "vehicle";
    [Tooltip("Nom du modèle (affiché dans le HUD / l'UI).")]
    public string modelName = "Vehicle";
    [Tooltip("Prefab conduisible (avec VehicleController) instancié pour ce véhicule.")]
    public GameObject prefab;

    [Header("Capacity")]
    [Tooltip("Nombre total de places assises (conducteur inclus).")]
    [Min(1)] public int passengerCount = 1;
    [Tooltip("Nombre d'emplacements de stockage du coffre.")]
    [Min(0)] public int trunkSlots = 0;

    [Header("Health")]
    [Tooltip("Points de vie maximum. À 0, le véhicule est KO (ne roule plus).")]
    [Min(1f)] public float maxHealth = 100f;

    [Header("Shop")]
    [Tooltip("Prix d'achat au concessionnaire (BroCoins).")]
    [Min(0)] public int price = 5000;

    [Header("Driving (kinematic arcade)")]
    public float maxSpeed     = 6f;
    public float reverseSpeed = 3f;
    public float acceleration = 8f;
    [Tooltip("Décélération au freinage actif (Espace ou marche arrière).")]
    public float braking      = 12f;
    [Tooltip("Décélération en roue libre (accélérateur relâché). Faible = inertie.")]
    public float friction     = 1.5f;
    [Tooltip("Vitesse de braquage (deg/s) à pleine vitesse.")]
    public float turnSpeed    = 90f;

    [Header("Sounds (optionnels — null = silencieux)")]
    [Tooltip("Boucle moteur, jouée tant que le véhicule est occupé ; le pitch suit la vitesse.")]
    public AudioClip engineLoop;
    [Tooltip("Joué une fois lors d'un freinage marqué.")]
    public AudioClip brake;
    [Tooltip("Klaxon (touche H).")]
    public AudioClip horn;
    [Tooltip("Ouverture de portière (à la montée).")]
    public AudioClip doorOpen;
    [Tooltip("Fermeture de portière (à la descente).")]
    public AudioClip doorClose;
    [Tooltip("Choc/collision (volume proportionnel à la violence de l'impact).")]
    public AudioClip impact;
    [Tooltip("Joué une fois quand le véhicule tombe KO (vie à 0).")]
    public AudioClip ko;
    [Tooltip("Verrouillage du véhicule.")]
    public AudioClip lockSound;
    [Tooltip("Déverrouillage du véhicule.")]
    public AudioClip unlockSound;
}
