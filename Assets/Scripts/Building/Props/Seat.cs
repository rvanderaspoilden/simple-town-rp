using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Sim.Building {
    public class Seat : Props {
        [Header("Settings")] [SerializeField] private Transform[] seatPositions;

        private Dictionary<int, int> playersAssociatedToSeatIdx;

        protected override void Awake() {
            base.Awake();

            this.playersAssociatedToSeatIdx = new Dictionary<int, int>();
        }

        protected override void Use() {
            int seatIdx = GetAvailableSeatIdx();

            if (seatIdx != -1) {
                photonView.RPC("RPC_AssignSeatForPlayer", RpcTarget.All, RoomManager.LocalPlayer.photonView.ViewID, seatIdx);
            }
        }

        [PunRPC]
        public bool RPC_AssignSeatForPlayer(int photonViewId, int seatIdx) {
            if (this.playersAssociatedToSeatIdx.Count == seatPositions.Length) {
                Debug.LogWarning("No place available on this seat");
                return false;
            }

            this.playersAssociatedToSeatIdx.Add(photonViewId, seatIdx);
            
            Debug.Log($"There is {this.playersAssociatedToSeatIdx.Count} seats used for {this.name}");

            // Sit if it's my player
            if (RoomManager.LocalPlayer.photonView.ViewID == photonViewId) {
                RoomManager.LocalPlayer.Sit(this, this.seatPositions[seatIdx]);
            }
            
            return true;
        }

        private int GetAvailableSeatIdx() {
            for (int i = 0; i < seatPositions.Length; i++) {
                if (!this.playersAssociatedToSeatIdx.ContainsValue(i)) return i;
            }

            return -1;
        }

        public bool RevokeSeatForPlayer(Player player) {
            if (!this.playersAssociatedToSeatIdx.ContainsKey(player.photonView.ViewID)) return false;

            this.playersAssociatedToSeatIdx.Remove(player.photonView.ViewID);

            return true;
        }
    }
}