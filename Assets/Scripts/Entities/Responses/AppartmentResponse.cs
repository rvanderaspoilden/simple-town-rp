using UnityEngine;

namespace Sim {
    [System.Serializable]
    public class AppartmentResponse {
        [SerializeField] private int uid;
        [SerializeField] private string owner;
        [SerializeField] private SceneData data;

        public int Uid {
            get => uid;
            set => uid = value;
        }

        public string Owner {
            get => owner;
            set => owner = value;
        }

        public SceneData Data {
            get => data;
            set => data = value;
        }
    }

}
