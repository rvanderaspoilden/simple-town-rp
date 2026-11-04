using UnityEngine;

/// <summary>
/// Item « balles » : pile de munitions À L'UNITÉ (maxStackSize 99). Un rechargement
/// (touche R ou tir à vide) consomme les balles MANQUANTES du chargeur d'une arme de la
/// MÊME famille (WeaponConfig.AmmoCategory) ; « Vider le chargeur » fait le chemin inverse.
/// Le serveur les cherche dans la poche du joueur, puis dans un sac/colis tenu en main.
/// Item standard par ailleurs : ramassable, stockable, empilable (poche comprise). L'icône
/// d'inventaire est une balle, le modèle 3D au sol/en main reste la caisse de munitions.
/// </summary>
[CreateAssetMenu(fileName = "New Ammo", menuName = "Configurations/Item/Ammo")]
public class AmmoConfig : ItemConfig {
    [Header("Munitions")]
    [Tooltip("Famille d'armes servie par ces balles : LIGHT = armes de poing, MEDIUM = fusils d'assaut, HEAVY = sniper.")]
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
