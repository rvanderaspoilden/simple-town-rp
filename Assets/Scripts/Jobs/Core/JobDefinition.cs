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

        [Tooltip("Durée maximale avant expiration (secondes). 0 = pas d'expiration.")]
        [SerializeField] private float expirationSeconds = 600f;

        [Tooltip("Nombre maximal de copies actives simultanément par joueur.")]
        [SerializeField] private int maxConcurrentPerPlayer = 1;

        public string JobId => jobId;
        public string DisplayNameKey => displayNameKey;
        public Sprite Icon => icon;
        public JobCategory Category => category;
        public IReadOnlyList<JobStepDefinition> Steps => steps;
        public IReadOnlyList<RewardDefinition> Rewards => rewards;
        public float ExpirationSeconds => expirationSeconds;
        public int MaxConcurrentPerPlayer => maxConcurrentPerPlayer;
    }
}
