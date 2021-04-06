using Photon.Pun;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class ApartmentManager : RoomManager {
        [Header("Only for debug")]
        [SerializeField]
        private Home homeData;

        public new static ApartmentManager Instance;

        protected override void Awake() {
            base.Awake();

            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
        }

        protected override void Save(SceneData sceneData) {
            ApiManager.Instance.SaveHomeScene(this.homeData, sceneData);
        }

        /*public override void InstantiateLocalCharacter(CharacterController prefab, CharacterData characterData, RoomNavigationData currentRoom, RoomNavigationData oldRoom) {
            base.InstantiateLocalCharacter(prefab, characterData, currentRoom, oldRoom);

            if (!PhotonNetwork.IsMasterClient && this.IsTenant(characterData)) {
                PhotonNetwork.CurrentRoom.SetMasterClient(PhotonNetwork.LocalPlayer);
            }
        }*/

        public Home HomeData {
            get => homeData;
            set => homeData = value;
        }

        public bool IsTenant(CharacterData character)
        {
            return character.Id == this.homeData.Tenant;
        }
    }
}