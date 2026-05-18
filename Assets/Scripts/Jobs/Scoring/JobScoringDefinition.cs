using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Règle de scoring attachée à un JobDefinition. Produit un JobRating à la
    /// complétion de la mission. Null sur le job = Perfect (récompense pleine).
    /// </summary>
    public abstract class JobScoringDefinition : ScriptableObject {
        public abstract JobRating Evaluate(JobInstance job);

        /// <summary>
        /// UI variant à afficher pour ce scorer. Le panneau client sélectionne
        /// la sous-view correspondante.
        /// </summary>
        public abstract JobResultVariant ResultVariant { get; }
    }
}
