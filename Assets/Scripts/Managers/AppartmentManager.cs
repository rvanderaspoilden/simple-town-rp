using UnityEngine;

namespace Sim
{
    public class AppartmentManager : RoomManager
    {
        [Header("Only for debug")]
        [SerializeField] private int id;
        [SerializeField] private string owner;

        public static AppartmentManager instance;
        protected override void Awake()
        {
            base.Awake();

            instance = this;
        }

        public void SetAppartmentData(string owner, int id)
        {
            this.id = id;
            this.owner = owner;
        }

        public int ID
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
