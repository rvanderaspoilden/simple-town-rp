/// <summary>
/// Messages toast centralisés du système inventaire. Référencés par le client (ItemSlot,
/// InventoryUI, ContainerPanelUI) ET le serveur (HandPlaceContext, PocketPlaceContext) pour
/// que le même refus produise le même texte, quel que soit le chemin (double-clic vs
/// drag&amp;drop, rejet client immédiat vs rejet serveur après round-trip).
/// </summary>
public static class InventoryToasts
{
    public const string NoSpaceInInventory   = "Pas de place dans l'inventaire";
    public const string ContainerFull        = "Conteneur plein";
    public const string NotPocketable        = "Cet item ne peut pas être stocké en poche";
    public const string NotStorable          = "Cet item ne peut pas être stocké";
    public const string NoNestedStorage      = "Un objet de stockage ne peut pas en contenir un autre";
    public const string NestedStorageMustBeEmpty = "Videz ce conteneur avant de le ranger";
    public const string OtherHandBusy        = "L'autre main n'est pas libre";
    public const string AlreadyHoldingTwoHand = "Vous portez déjà un objet à deux mains";
    public const string SlotFull             = "Slot plein";
    public const string StackPickOneAtATime  = "Pile — prends 1 à la fois";
}
