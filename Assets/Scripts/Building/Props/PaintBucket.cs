using System.Linq;
using Photon.Pun;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class PaintBucket : Props {
        [Header("Bucket Settings")]
        [SerializeField] private Color color = Color.white;

        [Header("Bucket settings debug")]
        [SerializeField] private PaintConfig paintConfig;

        public delegate void OnOpen(PaintBucket bucketOpened);

        public static event OnOpen OnOpened;

        protected override void SetupActions() {
            base.SetupActions();

            // todo replace it by appartment owner
            this.actions.ToList().ForEach(action => action.SetIsLocked(!PhotonNetwork.IsMasterClient));
        }

        public override void Use() {
            OnOpened?.Invoke(this);
        }

        public PaintConfig GetPaintConfig() {
            return this.paintConfig;
        }

        public void SetColor(Color color) {
            photonView.RPC("RPC_SetColor", RpcTarget.AllBuffered, new float[4] {color.r, color.g, color.b, color.a});
        }

        public void SetColor(float[] color) {
            photonView.RPC("RPC_SetColor", RpcTarget.AllBuffered, color);
        }

        [PunRPC]
        public void RPC_SetColor(float[] color) {
            this.color = new Color(color[0], color[1], color[2], color[3]);
        }

        public Color GetColor() {
            return this.color;
        }

        public void SetPaintConfig(PaintConfig config) {
            this.paintConfig = config;
        }

        public void SetPaintConfigId(int id) {
            photonView.RPC("RPC_SetPaintInside", RpcTarget.AllBuffered, id);
        }

        [PunRPC]
        public void RPC_SetPaintInside(int paintId) {
            this.paintConfig = DatabaseManager.PaintDatabase.GetPaintById(paintId);
        }
    }
}