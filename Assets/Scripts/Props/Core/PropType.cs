/// <summary>
/// Catégorise le comportement réseau d'un prop.
/// Détermine quel struct de state désérialiser et quel behaviour dispatcher.
/// </summary>
public enum PropType : byte {
    Generic      = 0,   // mobilier standard : built + preset, pas d'interaction spéciale
    Door         = 1,   // porte : isOpen géré par triggers serveur
    Seat         = 2,   // siège / canapé : occupancy par netId
    Dispenser    = 3,   // distributeur : catalogue en ScriptableObject, achat via C2S
    PaintBucket  = 4,   // seau de peinture : paintConfigId + couleur
    DeliveryBox  = 5,   // boîte de livraison : deliveryCount + ouverture via REST
    Package      = 6,   // colis : contient un PropsConfig à déballer, ouverture client-local
}
