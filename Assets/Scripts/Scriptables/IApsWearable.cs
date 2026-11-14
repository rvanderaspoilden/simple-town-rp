using AdvancedPeopleSystem;

/// <summary>
/// Item dont le visuel PORTÉ est un élément APS2 du personnage (catégorie + index de preset) :
/// vêtements (<see cref="ClothingConfig"/>, place wearables) et sacs à dos
/// (<see cref="Sim.Scriptables.BackpackConfig"/>, élément Item1, item classique porté au dos).
/// Les deux familles ont des mécaniques d'inventaire différentes (héritage simple : un sac est
/// d'abord un conteneur), mais partagent cette facette « essayable » : la cabine d'essayage ne
/// manipule QUE cette interface pour filtrer, grouper en onglets et prévisualiser.
/// </summary>
public interface IApsWearable {
    /// <summary>Catégorie APS2 habillée (Hat, Shirt, Pants, Shoes, Accessory, Item1 = sac).</summary>
    CharacterElementType ElementType { get; }

    /// <summary>Index du preset dans les CharacterSettings APS2 du genre porté.</summary>
    int ElementIndex { get; }

    /// <summary>Cet article est-il portable par un personnage de ce genre ?</summary>
    bool SupportsGender(Gender wearerGender);
}
