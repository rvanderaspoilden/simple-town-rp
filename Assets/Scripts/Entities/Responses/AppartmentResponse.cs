using UnityEngine;

namespace Sim {
    [System.Serializable]
    public class AppartmentResponse {
        [SerializeField] private string uid;
        [SerializeField] private string owner;
        [SerializeField] private SceneData data;
        
        public AppartmentResponse() {}

        public AppartmentResponse(string uid, string owner, SceneData data)
        {
            this.uid = uid;
            this.owner = owner;
            this.data = data;
        }

        public void SetUid(string value)
        {
            this.uid = value;
        }

        public string GetUid()
        {
            return this.uid;
        }

        public void SetOwner(string owner)
        {
            this.owner = owner;
        }

        public string GetOwner()
        {
            return this.owner;
        }

        public void SetData(SceneData sceneData)
        {
            this.data = sceneData;
        }

        public SceneData GetData()
        {
            return this.data;
        }
    }

}
