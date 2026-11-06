using UnityEngine;

namespace Sim.Scriptables {
    /// <summary>
    /// Sac à dos portable (slot dos). Sous-classe d'<see cref="ItemContainerConfig"/> : la grille
    /// vient du <see cref="ContainerConfig"/> (place item_container:{uuid} — le contenu suit le
    /// sac quand on le déplace/échange). Le visuel PORTÉ est géré comme un vêtement : l'élément
    /// APS2 Item1 (skinné, déjà configuré sur le personnage) est activé/désactivé — rien n'est
    /// instancié ni positionné ; le prefab monde générique reste caché tant que le sac est au dos.
    /// </summary>
    [CreateAssetMenu(menuName = "Configurations/Item/Backpack", fileName = "New Backpack")]
    public class BackpackConfig : ItemContainerConfig {
        [Header("Visuel porté (dos)")]
        [Tooltip("Index du preset APS2 Item1 activé quand le sac est porté.")]
        [SerializeField] private int apsElementIndex;

        public int ApsElementIndex => apsElementIndex;
    }
}
