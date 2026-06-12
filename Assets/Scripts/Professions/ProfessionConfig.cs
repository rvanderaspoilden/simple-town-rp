using UnityEngine;

namespace Sim.Professions {
    /// <summary>
    /// Source unique de vérité pour un métier (« profession »).
    ///
    /// Un asset par métier sous <c>Resources/Configurations/Professions/</c>. Référencé
    /// par <see cref="Sim.Missions.MissionDefinition"/> (missions), les composants de
    /// scène carrière (board, points, props), et l'arbre de compétences. L'identité
    /// canonique est <see cref="id"/> (clé runtime + wire DB).
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Profession", fileName = "Profession")]
    public class ProfessionConfig : ScriptableObject {
        [Tooltip("Identifiant stable utilisé comme clé de stockage backend " +
                 "(character_jobs.profession_id, characters.current_profession_id, " +
                 "constellation_states.points) et sur le fil de communication. " +
                 "Ex: \"delivery_driver\". Ne pas renommer une fois en production.")]
        public string id;

        [Tooltip("Libellé utilisateur en français. Ex: \"Livreur\".")]
        public string displayName;

        [TextArea]
        public string description;

        [Tooltip("Salaire de base versé périodiquement (toutes les City.salary_period_seconds) " +
                 "par PlayerCareerSalaryTicker.")]
        [Min(0)] public int baseSalary = 100;
    }
}
