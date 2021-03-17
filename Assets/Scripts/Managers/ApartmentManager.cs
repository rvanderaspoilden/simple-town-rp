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
            ApiManager.instance.SaveHomeScene(this.homeData, sceneData);
        }

        public override void InstantiateLocalCharacter(Character prefab, CharacterData characterData) {
            base.InstantiateLocalCharacter(prefab, characterData);

            if (!PhotonNetwork.IsMasterClient && this.IsOwner(characterData)) {
                PhotonNetwork.CurrentRoom.SetMasterClient(PhotonNetwork.LocalPlayer);
            }
        }

        public Home HomeData {
            get => homeData;
            set => homeData = value;
        }

        public bool IsOwner(CharacterData character) // TODO: check this
        {
            return character.Id == this.homeData.Owner;
        }
    }
}