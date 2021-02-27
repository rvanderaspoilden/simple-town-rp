using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Entities {
    [System.Serializable]
    public class CharacterData {
        [SerializeField]
        private string _id;

        [SerializeField]
        private string user_id;

        [SerializeField]
        private string firstname;

        [SerializeField]
        private string lastname;

        [SerializeField]
        private string origin_country;

        [SerializeField]
        private int appartment_id;

        [SerializeField]
        private string job;

        [SerializeField]
        private int money;

        [SerializeField]
        private VitalInformation vital_information;

        [SerializeField]
        private MoodEnum mood = MoodEnum.HAPPY;

        public MoodEnum Mood {
            get => mood;
            set => mood = value;
        }

        public string _Id {
            get => _id;
            set => _id = value;
        }

        public string UserId {
            get => user_id;
            set => user_id = value;
        }

        public string Job {
            get => job;
            set => job = value;
        }

        public int Money {
            get => money;
            set => money = value;
        }

        public VitalInformation VitalInformation {
            get => vital_information;
            set => vital_information = value;
        }

        public string Firstname {
            get => this.firstname.Substring(0, 1).ToUpper() + this.firstname.Substring(1, this.firstname.Length - 1);
            set => firstname = value;
        }

        public string Lastname {
            get => lastname.ToUpper();
            set => lastname = value;
        }

        public string OriginCountry {
            get => origin_country;
            set => origin_country = value;
        }

        public int AppartmentId {
            get => appartment_id;
            set => appartment_id = value;
        }

        public string GetFullName() {
            return $"{this.Firstname} {this.Lastname}";
        }
    }
}