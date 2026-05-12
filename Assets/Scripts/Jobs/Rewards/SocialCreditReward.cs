using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Récompense de crédit social. Encode la règle de design "toute
    /// interaction bénéficie aux DEUX participants" : on attribue séparément
    /// un montant à l'owner et au target — l'incrément target n'a d'effet
    /// que si le target est un joueur (les PNJ ignorent silencieusement).
    ///
    /// Stub : le composant PlayerSocialCredit n'existe pas encore. Pour
    /// l'instant la récompense logge uniquement les montants. À brancher
    /// quand le composant sera ajouté au joueur.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Jobs/Rewards/Social Credit", fileName = "SocialCreditReward")]
    public class SocialCreditReward : RewardDefinition {
        [Tooltip("Crédit social attribué au joueur qui exécute la mission.")]
        [Min(0)]
        [SerializeField] private int forOwner = 1;

        [Tooltip("Crédit social attribué à la cible primaire si c'est un joueur. Ignoré sinon.")]
        [Min(0)]
        [SerializeField] private int forPlayerTarget = 1;

        public int ForOwner => forOwner;
        public int ForPlayerTarget => forPlayerTarget;

        public override void Apply(JobInstance job) {
            if (job == null) return;

            if (forOwner > 0) {
                // TODO: brancher sur PlayerSocialCredit.Add(forOwner) côté owner
                GameLogger.System.Info("SocialCreditOwner_Stub {NetId} {Amount} {JobId}",
                    job.OwnerNetId, forOwner, job.Definition.JobId);
            }

            var primary = job.Context.primaryTarget;
            if (forPlayerTarget > 0 && primary != null && primary.Kind == JobTargetKind.Player) {
                // TODO: brancher sur PlayerSocialCredit.Add(forPlayerTarget) côté target
                GameLogger.System.Info("SocialCreditTarget_Stub {TargetId} {Amount} {JobId}",
                    primary.TargetId, forPlayerTarget, job.Definition.JobId);
            }
        }
    }
}
