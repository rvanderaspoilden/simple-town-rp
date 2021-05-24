using System;
using Sim.Enums;
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
        private Gender gender;

        [SerializeField]
        private Style style;

        [SerializeField]
        private MoodEnum mood = MoodEnum.HAPPY;

        public MoodEnum Mood {
            get => mood;
            set => mood = value;
        }

        public Gender Gender {
            get => gender;
            set => gender = value;
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
    }
}