using System.Collections.Generic;
using Sim.Constellation;
using Sim.Professions;
using UnityEngine;
using UnityEngine.Serialization;

namespace Sim.Missions {
    /// <summary>
    /// Définition statique d'un métier / d'une mission (ScriptableObject).
    /// Aucune logique runtime : c'est une recette composée de steps et de
    /// récompenses. Les instances vivent dans MissionInstance.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Missions/Mission Definition", fileName = "NewMissionDefinition")]
    public class MissionDefinition : ScriptableObject {
        [Tooltip("Identifiant stable utilisé en réseau et en persistance. Non-UI. " +
                 "FormerlySerializedAs(\"jobId\") garantit que les assets historiques " +
                 "(YAML key \"jobId\") chargent toujours sans perte de données.")]
        [FormerlySerializedAs("jobId")]
        [SerializeField] private string missionId;

        [SerializeField] private string displayNameKey;
        [SerializeField] private Sprite icon;
        [Tooltip("Métier auquel appartient cette mission. Source unique pour la catégorie (board, " +
                 "career gate) et pour le salaire de base — tous deux résolus via ProfessionConfig.")]
        [SerializeField] private ProfessionConfig profession;

        [Tooltip("Nœud de constellation requis pour PRENDRE cette mission. Null = aucune " +
                 "restriction. Le serveur refuse la prise si le joueur n'a pas débloqué ce nœud.")]
        [SerializeField] private ConstellationNodeData requiredNode;

        [Tooltip("Steps exécutés en séquence.")]
        [SerializeField] private List<MissionStepDefinition> steps = new List<MissionStepDefinition>();

        [Tooltip("Récompenses additives appliquées à la complétion. Chaque entrée couple un " +
                 "RewardKind (asset partagé, ex MoneyReward.asset) à un montant spécifique à cette " +
                 "mission. RewardSystem itère cette liste et appelle Kind.Apply(job, amount).")]
        [SerializeField] private List<RewardEntry> rewardEntries = new List<RewardEntry>();

        // ── LEGACY rewards ───────────────────────────────────────────────────
        // Liste héritée des RewardDefinition par paire {type, amount}. Conservée
        // pendant la migration vers rewardEntries — RewardSystem la lit en fallback
        // si rewardEntries est vide. À supprimer une fois toutes les missions migrées
        // via Tools → Mission → Migrate Rewards.
        [SerializeField] private List<RewardDefinition> rewards = new List<RewardDefinition>();

        [Tooltip("Règle de scoring optionnelle utilisée par ScoreModulatedMoneyReward. Null = rating Perfect (récompense pleine).")]
        [SerializeField] private MissionScoringDefinition scoringDefinition;

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

        public string MissionId => missionId;
        public string DisplayNameKey => displayNameKey;
        public Sprite Icon => icon;
        public ProfessionConfig Profession => profession;
        // Id du métier de la mission (= ProfessionConfig.id). "" si aucun métier assigné.
        public string ProfessionId => profession != null ? profession.id : "";
        public ConstellationNodeData RequiredNode => requiredNode;
        // Id du nœud requis pour prendre la mission. "" si aucun gate de constellation.
        public string RequiredNodeId => requiredNode != null ? requiredNode.id : "";
        public IReadOnlyList<MissionStepDefinition> Steps => steps;
        public IReadOnlyList<RewardEntry> RewardEntries => rewardEntries;
        public IReadOnlyList<RewardDefinition> Rewards => rewards;
        public MissionScoringDefinition ScoringDefinition => scoringDefinition;
        public float ExpirationSeconds => expirationSeconds;
        public float BoardExpirationSeconds => boardExpirationSeconds;
        public int MaxConcurrentPerPlayer => maxConcurrentPerPlayer;
        public int MaxConcurrentGlobal => maxConcurrentGlobal;
    }
}
