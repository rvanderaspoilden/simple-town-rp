using System;
using System.Collections.Generic;
using System.Linq;
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

        public override void Synchronize(Photon.Realtime.Player playerTarget) {
            base.Synchronize(playerTarget);

            if (this.playersAssociatedToSeatIdx.Count > 0) {
                photonView.RPC("RPC_Setup", playerTarget, this.playersAssociatedToSeatIdx);
            }
        }

        [PunRPC]
        public void RPC_Setup(Dictionary<int, int> data) {
            this.playersAssociatedToSeatIdx = data;
        }

        [PunRPC]
        public void RPC_AssignSeatForPlayer(int photonViewId, int seatIdx) {
            if (this.playersAssociatedToSeatIdx.Count == seatPositions.Length) {
                Debug.LogWarning("No place available on this seat");
                return;
            }

            this.playersAssociatedToSeatIdx.Add(photonViewId, seatIdx);

            Debug.Log($"There is {this.playersAssociatedToSeatIdx.Count} seats used for {this.name}");

            // Sit if it's my player
            if (RoomManager.LocalPlayer.photonView.ViewID == photonViewId) {
                RoomManager.LocalPlayer.Sit(this, this.seatPositions[seatIdx]);
            }
        }

        /**
         * Retrieve the nearest seat idx from the player
         */
        private int GetAvailableSeatIdx() {
            var seatFound = seatPositions.Where((t, i) => !this.playersAssociatedToSeatIdx.ContainsValue(i))
                .OrderBy(t => Vector3.Distance(t.position, RoomManager.LocalPlayer.transform.position))
                .First();

            return seatFound ? Array.IndexOf(seatPositions, seatFound) : -1;
        }

        public void RevokeSeatForPlayer(Player player) {
            photonView.RPC("RPC_RevokeSeatForPlayer", RpcTarget.All, player.photonView.ViewID);
        }

        [PunRPC]
        public void RPC_RevokeSeatForPlayer(int photonViewId) {
            if (!this.playersAssociatedToSeatIdx.ContainsKey(photonViewId)) return;

            Debug.Log($"There is one seat available for {this.name}");

            this.playersAssociatedToSeatIdx.Remove(photonViewId);
        }
    }
}