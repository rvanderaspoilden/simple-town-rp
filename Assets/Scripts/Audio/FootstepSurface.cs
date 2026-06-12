using UnityEngine;

namespace Sim.Audio {
    /// <summary>
    /// Marqueur optionnel posé sur un collider de sol/prop pour déclarer le son de pas qu'il
    /// produit (bois, carrelage, tapis, extérieur…). Le <see cref="FootstepDriver"/> raycaste
    /// vers le bas à chaque pas et lit ce composant via GetComponentInParent ; sans marqueur,
    /// il retombe sur <see cref="SfxId.FootstepDefault"/>. Data-driven : aucun code à toucher,
    /// il suffit de poser le composant + choisir le SfxId.
    /// </summary>
    public class FootstepSurface : MonoBehaviour {
        [SerializeField] private SfxId surfaceSfx = SfxId.FootstepDefault;
        public SfxId SurfaceSfx => surfaceSfx;
    }
}
