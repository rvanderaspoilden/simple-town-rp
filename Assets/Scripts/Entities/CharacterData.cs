using System;
using System.Collections.Generic;
using Sim.Enums;
using Sim.Jobs;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class CharacterData {
        [SerializeField]
        private string _id;

        [SerializeField]
        private string user_id;

        [SerializeField]
        private Identity identity;

        [SerializeField]
        private int money;

        [SerializeField]
        private Health health;

        [SerializeField]
        private Style style;

        [SerializeField]
        private MoodEnum mood = MoodEnum.HAPPY;

        // -1 = unemployed, otherwise a JobCategory value. Backed by the
        // characters.current_job column (nullable smallint on the backend).
        [SerializeField]
        private int currentJob = -1;

        // Mirror of the character_jobs collection for this character. Hydrated
        // server-side before SetRawCharacterData; broadcast to all clients via
        // the existing JSON SyncVar pipeline.
        [SerializeField]
        private List<CharacterJobData> jobs = new List<CharacterJobData>();

        public MoodEnum Mood {
            get => mood;
            set => mood = value;
        }

        public Style Style {
            get => style;
            set => style = value;
        }

        public string Id {
            get => _id;
            set => _id = value;
        }

        public string UserId {
            get => user_id;
            set => user_id = value;
        }

        public Identity Identity {
            get => identity;
            set => identity = value;
        }

        public int Money {
            get => money;
            set => money = value;
        }

        public Health Health {
            get => health;
            set => health = value;
        }

        public int CurrentJobRaw {
            get => currentJob;
            set => currentJob = value;
        }

        public JobCategory? CurrentJobCategory {
            get => currentJob < 0 ? (JobCategory?)null : (JobCategory)currentJob;
            set => currentJob = value.HasValue ? (int)value.Value : -1;
        }

        public List<CharacterJobData> Jobs {
            get => jobs ??= new List<CharacterJobData>();
            set => jobs = value ?? new List<CharacterJobData>();
        }

        public CharacterJobData GetJob(JobCategory category) {
            var list = Jobs;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].Category == (int)category) return list[i];
            }
            return null;
        }
    }
}