using System;
using Mirror;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class Ground : Props {
        [Header("Ground settings")]
        [SyncVar(hook = nameof(SetPaintConfigId))]
        [SerializeField]
        private int paintConfigId;

        private new Renderer renderer;

        private int oldPaintConfigId;

        private bool preview;

        protected override void Awake() {
            base.Awake();

            this.renderer = GetComponent<Renderer>();
        }

        public override void OnStartClient() {
            this.ApplyPaint();
        }

        private void Update() {
            /*if (Input.GetKeyDown(KeyCode.T)) {
                this.ApplyModification();
            }*/
        }

        [Client]
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

            this.CmdApplyModification(this.paintConfigId);
        }

        [Command]
        public void CmdApplyModification(int newPaintConfigId) {
            this.paintConfigId = newPaintConfigId;
        }

        [Client]
        public void ResetPreview() {
            this.paintConfigId = this.oldPaintConfigId;
            this.ApplyPaint();
            this.preview = false;
        }

        public bool IsPreview() {
            return this.preview;
        }

        public int GetPaintConfigId() {
            return this.paintConfigId;
        }

        public void SetPaintConfigId(int _, int newPaintConfigId) {
            this.paintConfigId = newPaintConfigId;
            this.ApplyPaint();
        }

        private void ApplyPaint() {
            PaintConfig paintConfig = DatabaseManager.PaintDatabase.GetPaintById(this.paintConfigId);

            if (paintConfig) {
                this.renderer.material = paintConfig.GetMaterial();
            }
        }
    }
}