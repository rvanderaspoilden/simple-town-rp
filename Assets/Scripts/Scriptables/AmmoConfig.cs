using UnityEngine;

/// <summary>
/// Item « boîte de munitions » : consommée EN ENTIER par un rechargement (touche R ou tir
/// à vide) pour remplir le chargeur d'une arme de la MÊME famille (WeaponConfig.AmmoCategory).
/// Le serveur la cherche dans la poche du joueur, puis dans un sac/colis tenu en main.
/// Item standard par ailleurs : ramassable, stockable, empilable en conteneur.
/// </summary>
[CreateAssetMenu(fileName = "New Ammo", menuName = "Configurations/Item/Ammo")]
public class AmmoConfig : ItemConfig {
    [Header("Munitions")]
    [Tooltip("Famille d'armes servie par cette boîte : LIGHT = armes de poing, MEDIUM = fusils d'assaut, HEAVY = sniper.")]
    [SerializeField]
    private AmmoCategory ammoCategory;

    public AmmoCategory AmmoCategory => ammoCategory;

    /// <summary>Libellé FR d'une famille de munitions, partagé par les tooltips (arme et boîte).</summary>
    public static string CategoryLabel(AmmoCategory category) {
        switch (category) {
            case AmmoCategory.LIGHT:  return "légères (arme de poing)";
            case AmmoCategory.MEDIUM: return "moyennes (fusil d'assaut)";
            case AmmoCategory.HEAVY:  return "lourdes (fusil de précision)";
            default:                  return category.ToString();
        }
    }
}
