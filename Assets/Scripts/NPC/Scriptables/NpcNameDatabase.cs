using System.Collections.Generic;
using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// Listes de prénoms et noms pour la génération d'identité des NPC.
    /// À placer sous Assets/Resources/Configurations/Databases/ pour être chargeable.
    /// </summary>
    [CreateAssetMenu(menuName = "SimpleTown/NPC/NpcNameDatabase", fileName = "NpcNameDatabase")]
    public class NpcNameDatabase : ScriptableObject {
        [SerializeField] private List<string> firstNames = new List<string> {
            "Lucas", "Emma", "Hugo", "Léa", "Louis", "Chloé", "Jules", "Manon",
            "Adam", "Mila", "Raphaël", "Inès", "Arthur", "Sarah", "Gabriel", "Alice"
        };

        [SerializeField] private List<string> lastNames = new List<string> {
            "Martin", "Bernard", "Robert", "Petit", "Durand", "Leroy", "Moreau",
            "Simon", "Laurent", "Lefebvre", "Michel", "Garcia", "David", "Bertrand"
        };

        public IReadOnlyList<string> FirstNames => firstNames;
        public IReadOnlyList<string> LastNames  => lastNames;

        public string PickRandomFirstName() =>
            firstNames.Count == 0 ? "Anonyme" : firstNames[Random.Range(0, firstNames.Count)];

        public string PickRandomLastName() =>
            lastNames.Count == 0 ? "Inconnu" : lastNames[Random.Range(0, lastNames.Count)];
    }
}
