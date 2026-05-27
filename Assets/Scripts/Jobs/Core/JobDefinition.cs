using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    public enum JobCategory : byte {
        Delivery,
        Cleaning,
        Repair,
        Gardening,
        Concierge,
        Music,
        Custom
    }

    /// <summary>
    /// Définition statique d'un métier / d'une mission (ScriptableObject).
    /// Aucune logique runtime : c'est une recette composée de steps et de
    /// récompenses. Les instances vivent dans JobInstance.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Job Definition", fileName = "NewJobDefinition")]
    public class JobDefinition : ScriptableObject {
        [Tooltip("Identifiant stable utilisé en réseau et en persistance. Non-UI.")]
        [SerializeField] private string jobId;

        [SerializeField] private string displayNameKey;
        [SerializeField] private Sprite icon;
        [SerializeField] private JobCategory category;

        [Tooltip("Steps exécutés en séquence.")]
        [SerializeField] private List<JobStepDefinition> steps = new List<JobStepDefinition>();

        [Tooltip("Récompenses additives appliquées à la complétion.")]
        [SerializeField] private List<RewardDefinition> rewards = new List<RewardDefinition>();

        [Tooltip("Règle de scoring optionnelle utilisée par ScoreModulatedMoneyReward. Null = rating Perfect (récompense pleine).")]
        [SerializeField] private JobScoringDefinition scoringDefinition;

        [Tooltip("Durée maximale avant expiration (secondes) une fois la mission active. 0 = pas d'expiration.")]
        [SerializeField] private float expirationSeconds = 600f;

        [Tooltip("Durée pendant laquelle l'offre reste affichée sur le board avant expiration (secondes). 0 = pas d'expiration de l'offre.")]
        [SerializeField] private float boardExpirationSeconds = 180f;

        [Tooltip("Nombre maximal de copies actives simultanément par joueur.")]
        [SerializeField] private int maxConcurrentPerPlayer = 1;

        [Tooltip("Nombre maximal d'instances VIVANTES (Available/Offered/Active) de cette mission " +
                 "dans le monde entier, tous joueurs confondus. 0 = illimité. À régler sur le nombre " +
                 "de spots physiques (ex. 1 machine d'emballage / 1 étagère de tri → 1 ; ajoute des " +
                 "machines plus tard → augmente la valeur).")]
        [Min(0)]
        [SerializeField] private int maxConcurrentGlobal = 0;

        [Tooltip("Salaire versé périodiquement aux joueurs qui ont ce métier comme métier actif. Le délai entre deux versements est configuré sur la City (salary_period_seconds).")]
        [Min(0)]
        [SerializeField] private int salaryAmount = 100;

        public string JobId => jobId;
        public string DisplayNameKey => displayNameKey;
        public Sprite Icon => icon;
        public JobCategory Category => category;
        public IReadOnlyList<JobStepDefinition> Steps => steps;
        public IReadOnlyList<RewardDefinition> Rewards => rewards;
        public JobScoringDefinition ScoringDefinition => scoringDefinition;
        public float ExpirationSeconds => expirationSeconds;
        public float BoardExpirationSeconds => boardExpirationSeconds;
        public int MaxConcurrentPerPlayer => maxConcurrentPerPlayer;
        public int MaxConcurrentGlobal => maxConcurrentGlobal;
        public int SalaryAmount => salaryAmount;
    }
}
