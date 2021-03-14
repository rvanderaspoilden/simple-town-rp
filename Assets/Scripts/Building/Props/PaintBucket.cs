using System.Linq;
using Photon.Pun;
using Photon.Realtime;
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

        protected override void SetupActions() {
            base.SetupActions();

            bool isOwner = AppartmentManager.instance && AppartmentManager.instance.IsOwner(NetworkManager.Instance.CharacterData);
            this.actions.ToList().ForEach(action => action.SetIsLocked(!isOwner));
        }

        protected override void Use() {
            OnOpened?.Invoke(this);
        }

        public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);

            this.SetPaintConfigId(this.paintConfig.GetId(), playerTarget);
            this.SetColor(this.color, playerTarget);
        }

        public PaintConfig GetPaintConfig() {
            return this.paintConfig;
        }

        public void SetColor(Color color, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetColor", rpcTarget, new float[4] {color.r, color.g, color.b, color.a});
        }

        public void SetColor(Color color, Player player) {
            photonView.RPC("RPC_SetColor", player, new float[4] {color.r, color.g, color.b, color.a});
        }

        public void SetColor(float[] color, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetColor", rpcTarget, color);
        }

        public void SetColor(float[] color, Player player) {
            photonView.RPC("RPC_SetColor", player, color);
        }

        [PunRPC]
        public void RPC_SetColor(float[] color) {
            if (color != null && color.Length == 4) {
                this.color = new Color(color[0], color[1], color[2], color[3]);
            }
        }

        public Color GetColor() {
            return this.color;
        }

        public void SetPaintConfig(PaintConfig config) {
            this.paintConfig = config;
        }

        public void SetPaintConfigId(int id, RpcTarget rpcTarget) {
            photonView.RPC("RPC_SetPaintInside", rpcTarget, id);
        }

        public void SetPaintConfigId(int id, Player player) {
            photonView.RPC("RPC_SetPaintInside", player, id);
        }

        [PunRPC]
        public void RPC_SetPaintInside(int paintId) {
            this.paintConfig = DatabaseManager.PaintDatabase.GetPaintById(paintId);
        }
    }
}