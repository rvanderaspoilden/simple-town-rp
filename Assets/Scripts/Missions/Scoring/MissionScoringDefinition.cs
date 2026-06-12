using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Règle de scoring attachée à un MissionDefinition. Produit un MissionRating à la
    /// complétion de la mission. Null sur le job = Perfect (récompense pleine).
    /// </summary>
    public abstract class MissionScoringDefinition : ScriptableObject {
        public abstract MissionRating Evaluate(MissionInstance job);
    }
}
