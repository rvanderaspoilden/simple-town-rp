using System;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Type de récompense (catégorie invariante) appliqué à la complétion d'une mission.
    /// Remplace <see cref="RewardDefinition"/> : un <b>SO unique par TYPE</b> (un asset
    /// MoneyReward pour tous les rewards d'argent, un asset XpReward, etc.) au lieu d'un
    /// asset par paire (type, montant).
    ///
    /// Le montant est désormais une donnée par mission, portée par <see cref="RewardEntry"/>
    /// dans la liste <c>rewards</c> de la MissionDefinition. <see cref="Apply"/> reçoit le
    /// <paramref name="authoredAmount"/> en paramètre — chaque sous-classe peut l'utiliser
    /// directement OU le modifier (rating, niveau, etc.) avant de l'attribuer.
    /// </summary>
    public abstract class RewardKind : ScriptableObject {
        public abstract void Apply(MissionInstance job, int authoredAmount);

        /// <summary>Représentation courte pour la UI (board, prévisualisation). Format
        /// libre, ex. "25 €" / "+1 social". Renvoyer vide pour ne pas afficher.</summary>
        public virtual string GetDisplayString(int authoredAmount) => string.Empty;
    }

    /// <summary>
    /// Entrée dans la liste <c>rewards</c> d'une MissionDefinition. Couple un type de récompense
    /// (SO <see cref="RewardKind"/>) à un montant authoré. Permet d'avoir des dizaines de
    /// missions qui partagent le même asset <c>MoneyReward.asset</c> en lui assignant un
    /// montant différent par mission, sans proliférer les SO.
    /// </summary>
    [Serializable]
    public class RewardEntry {
        public RewardKind kind;
        [Min(0)] public int amount;
    }
}
