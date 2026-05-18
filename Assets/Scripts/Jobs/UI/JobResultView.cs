using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Vue d'affichage de résultat de mission. Une sous-vue par
    /// JobResultVariant est posée comme enfant du
    /// JobCompletionResultPanel ; le panneau active la bonne en fonction
    /// du scorer ayant produit le rating.
    /// </summary>
    public abstract class JobResultView : MonoBehaviour {
        public abstract JobResultVariant Variant { get; }
        public abstract void Render(JobClientState state);
    }
}
