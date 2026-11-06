using AdvancedPeopleSystem;
using UnityEngine;

/// <summary>
/// Item vêtement/équipement GENRÉ : chaque asset est la version homme OU femme d'un élément
/// de garde-robe APS2 (<see cref="CharacterElementType"/> + index de preset dans les
/// CharacterSettings du genre). Un vêtement ne s'équipe que sur un personnage du même genre
/// (validation serveur WearablesPlaceContext via <see cref="SupportsGender"/>).
/// Les assets sont générés par Tools ▸ The BROZ ▸ APS2 ▸ Generate Clothing Item Configs
/// (ids stables : HOMME 200-299, FEMME 300-399 — base de catégorie + index de preset —
/// NE PAS les changer, ils sont persistés en base).
/// </summary>
[CreateAssetMenu(menuName = "Configurations/Clothing Item", fileName = "New Clothing Item")]
public class ClothingConfig : ItemConfig {
    [Header("APS2")]
    [Tooltip("Catégorie d'élément APS2 que cet item habille (Hat, Shirt, Pants…).")]
    [SerializeField]
    private CharacterElementType elementType;

    [Tooltip("Index du preset dans les CharacterSettings APS2 du genre de cet item.")]
    [SerializeField]
    private int elementIndex;

    [Tooltip("Genre de ce vêtement : il ne s'équipe que sur un personnage de ce genre.")]
    [SerializeField]
    private Gender gender = Gender.MALE;

    public CharacterElementType ElementType => elementType;

    public int ElementIndex => elementIndex;

    public Gender Gender => gender;

    /// <summary>Ce vêtement est-il portable par un personnage de ce genre ?</summary>
    public bool SupportsGender(Gender wearerGender) {
        return wearerGender == gender;
    }

    /// <summary>Nombre de slots de la place « wearables » (un par catégorie équipable).</summary>
    public const int WearSlotCount = 5;

    /// <summary>Slot de la place « wearables » occupé par cet item (0=chapeau, 1=accessoire,
    /// 2=haut, 3=pantalon, 4=chaussures). Ordre FIXE — les slotIndex sont persistés en DB.</summary>
    public int WearSlotIndex => WearSlotFor(elementType);

    public static int WearSlotFor(CharacterElementType type) {
        switch (type) {
            case CharacterElementType.Hat: return 0;
            case CharacterElementType.Accessory: return 1;
            case CharacterElementType.Shirt: return 2;
            case CharacterElementType.Pants: return 3;
            case CharacterElementType.Shoes: return 4;
            default: return -1;
        }
    }

    /// <summary>Catégorie APS2 portée par un slot wearables (inverse de WearSlotFor).</summary>
    public static CharacterElementType ElementTypeForSlot(int slotIndex) {
        switch (slotIndex) {
            case 0: return CharacterElementType.Hat;
            case 1: return CharacterElementType.Accessory;
            case 2: return CharacterElementType.Shirt;
            case 3: return CharacterElementType.Pants;
            case 4: return CharacterElementType.Shoes;
            default: return CharacterElementType.Hat;
        }
    }
}
