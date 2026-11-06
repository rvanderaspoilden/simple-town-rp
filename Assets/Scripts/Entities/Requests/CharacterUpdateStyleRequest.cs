using System;

namespace Sim.Entities {
    /// <summary>PUT /characters/:id/update-style — réécrit le style persisté (genre + setup
    /// APS2). Utilisé par le serveur Unity pour la migration garde-robe → items wearables.</summary>
    [Serializable]
    public struct CharacterUpdateStyleRequest {
        public Style style;
    }
}
