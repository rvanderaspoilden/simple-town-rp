using System;
using Sim.Utils;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public struct CharacterCreationRequest {
        [SerializeField]
        private string firstname;

        [SerializeField]
        private string lastname;

        [SerializeField]
        private string originCountry;

        [SerializeField]
        private string entranceDate;

        public CharacterCreationRequest(string firstname, string lastname, string originCountry) {
            this.firstname = firstname;
            this.lastname = lastname;
            this.originCountry = originCountry;
            this.entranceDate = CommonUtils.GetDate();
        }
    }
}