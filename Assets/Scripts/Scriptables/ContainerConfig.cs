using System.Collections.Generic;
using UnityEngine;

namespace Sim.Scriptables {
    /// <summary>
    /// Configuration de conteneur de stockage. Partagée par les meubles (PropsConfig.container),
    /// les coffres de véhicule (VehicleConfig.trunk) et les items-conteneurs « colis »
    /// (ItemContainerConfig.container). Dès que <see cref="slotCount"/> &gt; 0 on associe au
    /// porteur une Place côté backend (placeKey = "container:&lt;uuid&gt;" pour props/coffres,
    /// "item_container:&lt;uuid&gt;" pour colis) où les items glissés sont persistés.
    /// </summary>
    [System.Serializable]
    public class ContainerConfig {
        [Tooltip("Nombre de slots de la grille. 0 = pas un conteneur (champ ignoré).")]
        [Min(0)]
        [SerializeField] private int slotCount = 0;

        [Tooltip("Types d'items acceptés dans le conteneur. Liste vide = TOUS les types autorisés. " +
                 "Sert au filtrage côté UI (drop rejeté) et au gating côté serveur (drop ignoré).")]
        [SerializeField] private List<ItemType> acceptedTypes = new List<ItemType>();

        [Tooltip("Autorise l'emballage de meubles (props) dans ce conteneur via l'action « Emballer ». " +
                 "Le prop est déplacé (UUID conservé) dans la place du conteneur, position null, is_built remis à false.")]
        [SerializeField] private bool acceptsProps = false;

        [Tooltip("Son d'ouverture spécifique à ce conteneur (carton, frigo…). Vide = SfxId.ContainerOpen générique.")]
        [SerializeField] private AudioClip openClip;
        [Tooltip("Son de fermeture spécifique à ce conteneur. Vide = SfxId.ContainerClose générique.")]
        [SerializeField] private AudioClip closeClip;

        [Tooltip("Autorise de ranger des conteneurs NON VIDES (colis remplis…) à l'intérieur — imbrication. " +
                 "Défaut false (règle « videz-le d'abord »). Activé pour le coffre de véhicule.")]
        [SerializeField] private bool allowsNestedContainers = false;

        public int SlotCount => slotCount;
        public IReadOnlyList<ItemType> AcceptedTypes => acceptedTypes;
        public bool IsContainer => slotCount > 0;
        public bool AcceptsProps => acceptsProps;
        public bool AllowsNestedContainers => allowsNestedContainers;
        public AudioClip OpenClip => openClip;
        public AudioClip CloseClip => closeClip;

        /// <summary>True si le conteneur accepte ce type (liste vide = accepte tout).</summary>
        public bool Accepts(ItemType type)
            => acceptedTypes == null || acceptedTypes.Count == 0 || acceptedTypes.Contains(type);
    }
}
