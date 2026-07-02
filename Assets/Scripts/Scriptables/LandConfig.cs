using UnityEngine;

namespace Sim.Scriptables {
    /// <summary>
    /// Authored description of a buyable land parcel (price, size, presentation). One asset per lot,
    /// referenced by its <see cref="LandParcelController"/>. The persistence place key is derived as
    /// <c>land:{id}</c> — keep <see cref="id"/> stable once a lot has been sold.
    /// Assets live under <c>Assets/Resources/Configurations/Lands/</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "New Land", menuName = "Configurations/Land")]
    public class LandConfig : ScriptableObject {
        [Tooltip("Identifiant unique du lot. Sert à dériver la place de persistance (land:{id}). Ne pas changer après vente.")]
        public int id;

        [Tooltip("Nom affiché du lot (pancarte, UI).")]
        public string displayName = "Terrain";

        [Tooltip("Prix d'achat du terrain.")]
        public int price = 1000;

        [Tooltip("Loyer prélevé par période (in-game) lorsque le terrain est loué plutôt qu'acheté.")]
        public int rentPrice = 100;

        [Tooltip("Surface du lot (m²) — purement informatif pour l'UI.")]
        public float surface = 25f;

        [Tooltip("Visuel du lot pour l'UI (optionnel).")]
        public Sprite sprite;

        [TextArea]
        [Tooltip("Description affichée à l'achat.")]
        public string description;

        /// <summary>Place key dérivée — stable tant que l'id ne change pas.</summary>
        public string PlaceKey => $"land:{id}";
    }
}
