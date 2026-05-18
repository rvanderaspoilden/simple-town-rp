using System;
using UnityEngine;

namespace Sim.Entities {
    /// <summary>
    /// Mirror of the backend `character_jobs` row. One per (character, JobCategory)
    /// — stores XP and the date the player first applied to this job. Future
    /// columns (level, milestones, missions_completed, …) land here.
    /// Persisted via /character-jobs/* endpoints; broadcasted to clients as part
    /// of CharacterData (denormalized for SyncVar JSON simplicity).
    /// </summary>
    [Serializable]
    public class CharacterJobData {
        [SerializeField] private string _id;
        [SerializeField] private string character_id;
        [SerializeField] private int category;
        [SerializeField] private int xp;
        [SerializeField] private string started_at;

        public string Id => _id;
        public string CharacterId => character_id;
        public int Category {
            get => category;
            set => category = value;
        }
        public int Xp {
            get => xp;
            set => xp = value;
        }
        public string StartedAt {
            get => started_at;
            set => started_at = value;
        }
    }
}
