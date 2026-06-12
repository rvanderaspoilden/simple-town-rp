using UnityEngine;

namespace Sim.Missions {
    public enum MissionTargetKind : byte {
        Player,
        Npc,
        Apartment,
        Mailbox,
        Shop,
        Zone
    }

    /// <summary>
    /// Cible universelle d'une mission. Le système de jobs ne fait jamais de cast
    /// vers Player ou Npc — il passe par cette abstraction. Implémentations dans
    /// Assets/Scripts/Jobs/Targets/.
    /// </summary>
    public interface IMissionTarget {
        string TargetId { get; }
        MissionTargetKind Kind { get; }

        /// <summary>Transform serveur de la cible (jamais null tant que IsAvailable=true).</summary>
        Transform Transform { get; }

        /// <summary>False si la cible a despawn / s'est déconnectée / n'est plus interactible.</summary>
        bool IsAvailable { get; }

        /// <summary>Nom affiché côté HUD (PNJ FullName, joueur FullName, etc.). Peut être vide.</summary>
        string DisplayName { get; }
    }
}
