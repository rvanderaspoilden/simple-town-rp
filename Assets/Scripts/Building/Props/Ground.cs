using Photon.Pun;
using Photon.Realtime;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class Ground : Props {
        [Header("Ground settings")]
        [SerializeField]
        private int paintConfigId;

        private new Renderer renderer;

        private int oldPaintConfigId;

        private bool preview;

        protected override void Awake() {
            base.Awake();

            this.renderer = GetComponent<Renderer>();
        }

        protected override void Start() {
            base.Start();

            this.ApplyPaint();
        }

        /*public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);

            this.SetPaintConfigId(this.paintConfigId, playerTarget);
        }
        */

        public void Preview(PaintConfig paintConfig) {
            if (this.preview) {
                this.ResetPreview();
            } else {
                this.oldPaintConfigId = this.paintConfigId;
                this.paintConfigId = paintConfig.GetId();
                this.ApplyPaint();
                this.preview = true;
            }
        }

        public void ApplyModification() {
            this.oldPaintConfigId = this.paintConfigId;
            this.preview = false;
            //this.SetPaintConfigId(this.paintConfigId, RpcTarget.Others);
        }

        public void ResetPreview() {
            this.paintConfigId = this.oldPaintConfigId;
            this.ApplyPaint();
            this.preview = false;
        }

        public bool IsPreview() {
            return this.preview;
        }

        public PaintConfig GetPaintConfig() {
            return DatabaseManager.PaintDatabase.GetPaintById(this.paintConfigId);
        }

        public int GetPaintConfigId() {
            return this.paintConfigId;
        }

        /*public void SetPaintConfigId(int paintConfigId, RpcTarget rpcTarget) {
            this.photonView.RPC("RPC_SetPaintConfigId", rpcTarget, paintConfigId);
        }

        public void SetPaintConfigId(int paintConfigId, Player playerTarget) {
            this.photonView.RPC("RPC_SetPaintConfigId", playerTarget, paintConfigId);
        }*/

        [PunRPC]
        public void RPC_SetPaintConfigId(int id) {
            this.paintConfigId = id;
            this.ApplyPaint();
        }

        private void ApplyPaint() {
            if (this.GetPaintConfig()) {
                this.renderer.material = this.GetPaintConfig().GetMaterial();
            }
        }
    }
}