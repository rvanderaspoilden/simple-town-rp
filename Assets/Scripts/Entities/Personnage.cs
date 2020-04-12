using UnityEngine;

namespace Sim.Entities {
    [System.Serializable]
    public class Personnage {
        [SerializeField] private string firstname;
        [SerializeField] private string lastname;

        public Personnage(string firstname, string lastname) {
            this.firstname = firstname;
            this.lastname = lastname;
        }

        public string GetFirstname() {
            return this.firstname;
        }

        public string GetLastName() {
            return this.lastname;
        }
    }   
}
