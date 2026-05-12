using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Définition statique d'un step (ScriptableObject). Les implémentations
    /// concrètes vivent dans Assets/Scripts/Jobs/Steps/. Chaque sous-classe
    /// expose ses propres champs sérialisés (radius, item id, minigame ref…)
    /// et fabrique l'instance runtime correspondante.
    /// </summary>
    public abstract class JobStepDefinition : ScriptableObject {
        [SerializeField] private string promptKey;

        /// <summary>Clé de localisation affichée dans le HUD pendant ce step.</summary>
        public string PromptKey => promptKey;

        public abstract JobStepInstance CreateInstance(JobInstance owner);
    }
}
