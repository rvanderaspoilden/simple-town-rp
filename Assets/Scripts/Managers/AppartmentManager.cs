using UnityEngine;

namespace Sim
{
    public class AppartmentManager : RoomManager
    {
        [Header("Only for debug")] [SerializeField]
        private string id;

        [SerializeField] private string owner;

        public static AppartmentManager instance;

        protected override void Awake()
        {
            base.Awake();

            instance = this;
        }
        
        protected override void Save(SceneData sceneData)
        {
            ApiManager.instance.SaveAppartment(id, owner, sceneData);
        }

        public void SetAppartmentData(string owner, string id)
        {
            this.id = id;
            this.owner = owner;
        }

        public string ID
        {
            get => id;
            set => id = value;
        }

        public string Owner
        {
            get => owner;
            set => owner = value;
        }
    }
}