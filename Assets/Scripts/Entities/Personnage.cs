using UnityEngine;

namespace Sim.Entities {
    [System.Serializable]
    public class Personnage {
        [SerializeField] private string _id;
        [SerializeField] private string user_id;
        [SerializeField] private string firstname;
        [SerializeField] private string lastname;
        [SerializeField] private string origin_country;
        [SerializeField] private int appartment_id;

        public string _Id {
            get => _id;
            set => _id = value;
        }

        public string UserId {
            get => user_id;
            set => user_id = value;
        }

        public string Firstname {
            get => firstname;
            set => firstname = value;
        }

        public string Lastname {
            get => lastname;
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
    }
}