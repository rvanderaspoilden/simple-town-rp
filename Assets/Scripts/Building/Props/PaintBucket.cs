﻿using Photon.Pun;
using Sim.Enums;
using Sim.Interactables;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Building {
    public class PaintBucket : Props {
        [Header("Bucket Settings")]
        [SerializeField] private Action useAction;
        [SerializeField] private Color color = Color.white;

        [Header("Bucket settings debug")]
        [SerializeField] private PaintConfig paintConfig;
        
        public delegate void OnOpen(PaintBucket bucketOpened);

        public static event OnOpen OnOpened;

        protected override void SetupActions() {
            useAction.SetIsLocked(!PhotonNetwork.IsMasterClient); // todo replace it by appartment owner
            this.actions = new Action[1] {this.useAction};
        }

        public override void Use() {
            OnOpened?.Invoke(this);
        }

        public PaintConfig GetPaintConfig() {
            return this.paintConfig;
        }

        public void SetColor(Color color) {
            this.color = color;
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