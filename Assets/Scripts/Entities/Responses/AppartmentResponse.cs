using UnityEngine;

namespace Sim {
    [System.Serializable]
    public class AppartmentResponse {
        [SerializeField] private string id;
        [SerializeField] private string uid;
        [SerializeField] private string owner;
        [SerializeField] private SceneData data;

        public string Id {
            get => id;
            set => id = value;
        }

        public string Uid {
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
