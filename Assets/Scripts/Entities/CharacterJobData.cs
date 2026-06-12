using System;
using UnityEngine;

namespace Sim.Entities {
    /// <summary>
    /// Mirror of the backend `character_jobs` row. One per (character, profession) —
    /// stocke la date d'application initiale au métier. L'identité du métier est
    /// l'id de profession (ProfessionConfig.id). Persisté via /character-jobs/* ;
    /// broadcastée aux clients dans CharacterData (dénormalisé pour SyncVar JSON).
    /// </summary>
    [Serializable]
    public class CharacterJobData {
        [SerializeField] private string _id;
        [SerializeField] private string character_id;
        [SerializeField] private string profession_id;
        [SerializeField] private string started_at;

        public string Id => _id;
        public string CharacterId => character_id;
        public string ProfessionId {
            get => profession_id;
            set => profession_id = value;
        }
        public string StartedAt {
            get => started_at;
            set => started_at = value;
        }
    }
}
