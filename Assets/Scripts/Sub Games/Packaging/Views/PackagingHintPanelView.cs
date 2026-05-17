using UnityEngine;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Panneau d'aide mémo affiché en permanence pendant la phase de placement.
    /// Caché pendant l'affichage de la modale de résultat. Contenu 100% statique
    /// — configuré dans l'inspector (icônes + TextMeshPro). Ce script fournit
    /// uniquement le surface Show/Hide.
    ///
    /// Structure UI suggérée (à créer dans la scène) :
    ///   HintPanel (ce composant)
    ///   ├─ Row "Espace"    — icône 📦 + "Remplis bien la boîte → plus de points"
    ///   ├─ Row "Lourds"    — icône ⚖ + "Objets lourds restent en bas"
    ///   └─ Row "Fragiles"  — icône 🥚 + "Objets fragiles : rien de lourd au-dessus"
    /// </summary>
    public class PackagingHintPanelView : MonoBehaviour {
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
