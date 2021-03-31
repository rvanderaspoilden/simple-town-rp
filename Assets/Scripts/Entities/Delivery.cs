using System;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class Delivery {
        [SerializeField]
        private string _id;

        [SerializeField]
        private string recipientId;

        [SerializeField]
        private DeliveryType type;

        [SerializeField]
        private int paintConfigId;

        [SerializeField]
        private int propsConfigId;

        [SerializeField]
        private short[] color;

        public string ID => _id;

        public string RecipientId {
            get => recipientId;
            set => recipientId = value;
        }

        public DeliveryType Type {
            get => type;
            set => type = value;
        }

        public int PaintConfigId {
            get => paintConfigId;
            set => paintConfigId = value;
        }

        public int PropsConfigId {
            get => propsConfigId;
            set => propsConfigId = value;
        }

        public short[] Color {
            get => color;
            set => color = value;
        }

        public string DisplayName() {
            if (this.type == DeliveryType.PROPS) {
                return DatabaseManager.PropsDatabase.GetPropsById(this.propsConfigId).GetDisplayName();
            } else if(this.type == DeliveryType.COVER) {
                return DatabaseManager.PaintDatabase.GetPaintById(this.paintConfigId).GetDisplayName();
            }

            throw new Exception("error.delivery.type-not-found");
        }
    }

    public enum DeliveryType {
        PROPS,
        COVER
    }
}