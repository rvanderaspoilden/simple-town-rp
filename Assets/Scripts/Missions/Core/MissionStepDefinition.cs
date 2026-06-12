using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Définition statique d'un step (ScriptableObject). Les implémentations
    /// concrètes vivent dans Assets/Scripts/Jobs/Steps/. Chaque sous-classe
    /// expose ses propres champs sérialisés (radius, item id, minigame ref…)
    /// et fabrique l'instance runtime correspondante.
    /// </summary>
    public abstract class MissionStepDefinition : ScriptableObject {
        [SerializeField] private string promptKey;

        /// <summary>Clé de localisation affichée dans le HUD pendant ce step.</summary>
        public string PromptKey => promptKey;

        public abstract MissionStepInstance CreateInstance(MissionInstance owner);

        /// <summary>
        /// Clé de la cible active utilisée par ce step ("primary" / "secondary").
        /// Permet à l'HUD/indicateur de suivre le bon point quand on change de step.
        /// Override dans les step definitions qui ont un targetKey serialisé.
        /// </summary>
        public virtual string GetActiveTargetKey() => "primary";

        /// <summary>
        /// Vrai si la cible de ce step doit afficher le beacon monde du MissionPoint
        /// (la balise « va ici »). Réservé aux steps de navigation pure
        /// (Reach/Deliver) : pour les autres (UseMachine, Pickup, Sort…), la cible
        /// concrète est déjà mise en évidence par GetHighlightKinds(), donc le
        /// beacon ferait double emploi. False par défaut.
        /// </summary>
        public virtual bool ShowsTargetBeacon => false;

        // ── Mission highlight (outline monde) ─────────────────────────────────
        /// <summary>
        /// Types d'objets à mettre en évidence pendant ce step (voie "tous les
        /// objets de ce kind", filtrés par la carrière de la mission). None par
        /// défaut → aucun highlight monde (ex. steps de zone : Reach/Deliver,
        /// gérés par le GPS).
        /// </summary>
        public virtual MissionHighlightKind GetHighlightKinds() => MissionHighlightKind.None;

        /// <summary>
        /// Ids d'objets précis à mettre en évidence (voie "ciblage exact",
        /// bypass du filtre carrière). Vide par défaut. À overrider seulement
        /// quand un step doit pointer un objet unique plutôt que tout un kind.
        /// </summary>
        public virtual IReadOnlyList<string> GetHighlightTargetIds() => Array.Empty<string>();
    }
}
