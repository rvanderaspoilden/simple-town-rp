using System.Collections.Generic;
using AdvancedPeopleSystem;
using UnityEngine;

/// <summary>
/// Garde-robe de DÉPART : la liste des vêtements proposés à la création de personnage.
/// Le menu de création ne cycle que dans ces choix ; les vêtements choisis partent dans le
/// style à la création puis sont convertis en items équipés au premier join (migration
/// ServerItemManager.MigrateStyleWardrobeIntoItems). Tout le reste du catalogue (tenues de
/// métier…) se trouve en jeu.
/// Asset attendu : Resources/Configurations/StarterWardrobe (cf. <see cref="Load"/>).
/// </summary>
[CreateAssetMenu(menuName = "Configurations/Starter Wardrobe", fileName = "StarterWardrobe")]
public class StarterWardrobeConfig : ScriptableObject {
    public const string ResourcePath = "Configurations/StarterWardrobe";

    [SerializeField, Tooltip("Vêtements proposés à la création (toutes catégories confondues).")]
    private List<ClothingConfig> choices = new List<ClothingConfig>();

    public static StarterWardrobeConfig Load() => Resources.Load<StarterWardrobeConfig>(ResourcePath);

    /// <summary>Indices de preset APS2 autorisés pour une catégorie, triés. Vide = catégorie
    /// non proposée à la création.</summary>
    public List<int> AllowedElementIndices(CharacterElementType type) {
        var result = new List<int>();
        foreach (ClothingConfig c in choices) {
            if (c != null && c.ElementType == type && !result.Contains(c.ElementIndex)) result.Add(c.ElementIndex);
        }
        result.Sort();
        return result;
    }
}
