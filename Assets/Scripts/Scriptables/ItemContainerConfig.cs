using UnityEngine;

namespace Sim.Scriptables {
    /// <summary>
    /// Item qui est lui-même un conteneur (« colis », sac, etc.). Sous-classe d'<see cref="ItemConfig"/>
    /// — même pattern que <c>ConsumableConfig</c>/<c>FuelCanisterConfig</c> — pour ne PAS polluer la
    /// base avec des champs storage inutiles aux items non-conteneurs. La place backend dédiée est
    /// "item_container:{itemUuid}".
    /// </summary>
    [CreateAssetMenu(menuName = "Configurations/Item/Container", fileName = "New Item Container")]
    public class ItemContainerConfig : ItemConfig {
        [Tooltip("Grille du colis (slots, types acceptés, autorise meubles, sons d'ouverture/fermeture, " +
                 "imbrication). SlotCount > 0 obligatoire.")]
        [SerializeField] private ContainerConfig container = new ContainerConfig();

        public ContainerConfig Container => container;

        /// <summary>Helper concis pour tous les call sites qui ont juste un <see cref="ItemConfig"/>
        /// en main. Renvoie le <see cref="ContainerConfig"/> si le config concret est bien un
        /// <see cref="ItemContainerConfig"/>, null sinon (item non-conteneur).</summary>
        public static ContainerConfig Of(ItemConfig cfg) => (cfg as ItemContainerConfig)?.container;
    }
}
