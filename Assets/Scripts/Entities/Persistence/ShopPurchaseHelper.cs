namespace Sim.Entities.Persistence {
    /// <summary>
    /// Helper partagé pour matérialiser un prop acheté dans la place "transit" (POST /props).
    /// Utilisé par les deux flux d'achat "copie" : le phone shop (SimpleTownNetwork) et le
    /// magasin physique (PropInteractionDispatcher). Le prop reste en transit jusqu'à ce que
    /// l'acheteur consomme sa livraison et le construise dans son appartement.
    /// </summary>
    public static class ShopPurchaseHelper {
        /// <summary>
        /// Construit le body de base d'un prop (PROPS) en transit, possédé par le destinataire.
        /// position/rotation null → pas de placement physique tant qu'il n'est pas construit.
        /// Les deliveries de type COVER ajoutent ensuite leur stateData (paint config + couleur).
        /// </summary>
        public static CreatePropBody BuildTransitPropBody(string transitPlaceId, int configId, string recipientId, int presetId) =>
            new CreatePropBody {
                placeId     = transitPlaceId,
                configId    = configId,
                ownedBy     = recipientId,
                builtBy     = recipientId,
                presetIndex = presetId,
                position    = null,
                rotation    = null,
            };
    }
}
