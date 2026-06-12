using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Récompense appliquée à la complétion d'une mission. Externe au métier :
    /// RewardSystem parcourt MissionDefinition.rewards et appelle Apply sur chacune.
    /// </summary>
    public abstract class RewardDefinition : ScriptableObject {
        public abstract void Apply(MissionInstance job);

        /// <summary>
        /// Représentation courte pour la UI (board, prévisualisation). Format
        /// libre, ex. "25 €" / "+1 social". Renvoyer vide pour ne pas afficher.
        /// </summary>
        public virtual string GetDisplayString() => string.Empty;
    }
}
