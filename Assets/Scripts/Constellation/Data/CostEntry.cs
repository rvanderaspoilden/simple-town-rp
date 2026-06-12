using System;
using Sim.Constellation.Branches;

namespace Sim.Constellation {
    /// <summary>
    /// Une entrée de coût d'un nœud : N points d'une devise (branche). Le coût total d'un
    /// nœud est une <c>List&lt;CostEntry&gt;</c> — combinaison arbitraire de devises, sans
    /// distinction primaire/secondaire/métier. Liste vide = nœud gratuit.
    ///
    /// PAS un ScriptableObject (donc pas de contrainte 1:1 fichier/classe) ; juste une
    /// structure sérialisable imbriquée dans <see cref="ConstellationNodeData"/>.
    /// </summary>
    [Serializable]
    public class CostEntry {
        public BranchConfig branch;
        public int amount;
    }
}
