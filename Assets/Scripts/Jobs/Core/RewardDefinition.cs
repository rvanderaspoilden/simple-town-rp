using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Récompense appliquée à la complétion d'une mission. Externe au métier :
    /// le RewardSystem (à venir) parcourt JobDefinition.rewards et appelle Apply
    /// sur chacune. Implémentations concrètes dans Assets/Scripts/Jobs/Rewards/.
    /// </summary>
    public abstract class RewardDefinition : ScriptableObject {
        public abstract void Apply(JobInstance job);
    }
}
