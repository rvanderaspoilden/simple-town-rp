using System;
using System.Collections.Generic;
using Sim.Enums;
using Sim.Professions;
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

        // "" = unemployed, otherwise a ProfessionConfig.id. Backed by the
        // characters.current_profession_id column (nullable text on the backend).
        [SerializeField]
        private string currentProfessionId = "";

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

        // Id du métier actif (= ProfessionConfig.id). Chaîne vide = chômage.
        public string CurrentProfessionId {
            get => currentProfessionId;
            set => currentProfessionId = value ?? "";
        }

        // Résout le SO ProfessionConfig du métier actif via ProfessionDatabase.
        // Null si le joueur est au chômage. Donne accès au displayName + baseSalary.
        public ProfessionConfig CurrentProfession =>
            string.IsNullOrEmpty(currentProfessionId) ? null : ProfessionDatabase.ById(currentProfessionId);

        public List<CharacterJobData> Jobs {
            get => jobs ??= new List<CharacterJobData>();
            set => jobs = value ?? new List<CharacterJobData>();
        }

        public CharacterJobData GetJob(string professionId) {
            if (string.IsNullOrEmpty(professionId)) return null;
            var list = Jobs;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].ProfessionId == professionId) return list[i];
            }
            return null;
        }
    }
}