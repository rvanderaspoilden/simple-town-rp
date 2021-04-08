using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using Sim.Enums;
using Sim.Interactables;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class PaintBucket : Props {
        [Header("Bucket Settings")]
        [SerializeField]
        private Color color = Color.white;

        [Header("Bucket settings debug")]
        [SerializeField]
        private PaintConfig paintConfig;

        public delegate void OnOpen(PaintBucket bucketOpened);

        public static event OnOpen OnOpened;

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.PAINT)) {
                OnOpened?.Invoke(this);
            }
        }
        /*public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);

            this.SetPaintConfigId(this.paintConfig.GetId(), playerTarget);
            this.SetColor(this.color, playerTarget);
        }*/

        public PaintConfig GetPaintConfig() {
            return this.paintConfig;
        }

        /*public void SetColor(Color value, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetColor", rpcTarget, new float[3] {value.r, value.g, value.b});
        }

        public void SetColor(Color value, Player player) {
            photonView.RPC("RPC_SetColor", player, new float[3] {value.r, value.g, value.b});
        }

        public void SetColor(float[] value, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetColor", rpcTarget, value);
        }

        public void SetColor(float[] value, Player player) {
            photonView.RPC("RPC_SetColor", player, value);
        }*/

        [PunRPC]
        public void RPC_SetColor(float[] value) {
            if (value != null && value.Length >= 3) {
                this.color = new Color(value[0], value[1], value[2]);
            }
        }

        public Color GetColor() {
            return this.color;
        }

        public void SetPaintConfig(PaintConfig config) {
            this.paintConfig = config;
        }

        /*public void SetPaintConfigId(int id, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetPaintInside", rpcTarget, id);
        }

        public void SetPaintConfigId(int id, Player player) {
            photonView.RPC("RPC_SetPaintInside", player, id);
        }*/

        [PunRPC]
        public void RPC_SetPaintInside(int paintId) {
            this.paintConfig = DatabaseManager.PaintDatabase.GetPaintById(paintId);
        }
    }
}