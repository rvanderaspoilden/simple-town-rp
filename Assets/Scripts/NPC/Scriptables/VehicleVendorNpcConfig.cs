using System.Collections.Generic;
using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// NPC concessionnaire. Sous-classe de <see cref="MerchantNpcConfig"/> : hérite du
    /// comportement « stand » (état Merchant, tend/pause, interactable) sans y toucher — seul le
    /// catalogue diffère : des <see cref="VehicleConfig"/> au lieu d'items. Le catalogue d'items
    /// hérité reste simplement vide.
    ///
    /// Le prix vient de <c>VehicleConfig.price</c> (source unique — pas de prix répliqué ici).
    /// La boutique s'ouvre via <see cref="DialogueAction.OpenVehicleShop"/> dans le dialogue ;
    /// l'achat est validé/exécuté côté serveur par <see cref="Sim.VehicleSystemBootstrap"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Configurations/Vehicle Vendor NPC", fileName = "New Vehicle Vendor NPC")]
    public class VehicleVendorNpcConfig : MerchantNpcConfig {
        [Header("Concessionnaire")]
        [Tooltip("Véhicules en vente. Le prix affiché/débité est VehicleConfig.price.")]
        [SerializeField] private List<VehicleConfig> vehicleCatalog = new List<VehicleConfig>();

        public IReadOnlyList<VehicleConfig> VehicleCatalog => vehicleCatalog;

        /// <summary>
        /// Résout une entrée du catalogue par id de VehicleConfig. Sert au serveur pour valider
        /// qu'un véhicule demandé est bien en vente chez ce vendeur.
        /// </summary>
        public bool TryGetVehicle(string configId, out VehicleConfig cfg) {
            foreach (VehicleConfig v in vehicleCatalog) {
                if (v != null && v.id == configId) {
                    cfg = v;
                    return true;
                }
            }
            cfg = null;
            return false;
        }
    }
}
