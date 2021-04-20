using System;

namespace Sim.Entities {
    [Serializable]
    public struct Delivery {
        public string _id;

        public string recipientId;

        public DeliveryType type;

        public int paintConfigId;

        public int propsConfigId;

        public float[] color;

        public int propsPresetId;

        public string ID => _id;

        public string RecipientId {
            get => recipientId;
            set => recipientId = value;
        }

        public int PropsPresetId {
            get => propsPresetId;
            set => propsPresetId = value;
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

        public float[] Color {
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

    public enum DeliveryType : byte {
        PROPS,
        COVER
    }
}