using Mirror;

/// <summary>
/// Server → Owner. Envoyé juste après le spawn des colis d'un step de tri, pour
/// associer chaque entité d'item à sa <see cref="SortingCategory"/>. La catégorie
/// est une donnée PUREMENT métier (tri) : elle ne transite donc pas par le pipeline
/// d'items générique (S2C_SpawnItem / ItemEntity), mais par ce message dédié.
///
/// Le client résout chaque entityId via ClientItemManager et applique la catégorie
/// au PackageJobItemBehaviour du colis (pour l'étiquette). Émis sur la connexion de
/// l'owner après les S2C_SpawnItem correspondants → ordre garanti, l'item existe déjà.
/// </summary>
public struct JobSortItemsSpawnedMessage : NetworkMessage {
    public int[]             entityIds;
    public SortingCategory[] categories;
}
