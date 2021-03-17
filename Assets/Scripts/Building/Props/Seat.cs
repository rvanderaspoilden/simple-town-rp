﻿using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Sim.Building {
    public class Seat : Props {
        [Header("Settings")]
        [SerializeField]
        private Transform[] seatPositions;

        private Dictionary<int, int> charactersAssociatedToSeatIdx;

        protected override void Awake() {
            base.Awake();

            this.charactersAssociatedToSeatIdx = new Dictionary<int, int>();
        }

        protected override void Use() {
            int seatIdx = GetAvailableSeatIdx();

            if (seatIdx != -1) {
                photonView.RPC("RPC_AssignSeat", RpcTarget.All, RoomManager.LocalCharacter.photonView.ViewID, seatIdx);
            }
        }

        public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);

            if (this.charactersAssociatedToSeatIdx.Count > 0) {
                photonView.RPC("RPC_Setup", playerTarget, this.charactersAssociatedToSeatIdx);
            }
        }

        [PunRPC]
        public void RPC_Setup(Dictionary<int, int> data) {
            this.charactersAssociatedToSeatIdx = data;
        }

        [PunRPC]
        public void RPC_AssignSeat(int photonViewId, int seatIdx) {
            if (this.charactersAssociatedToSeatIdx.Count == seatPositions.Length) {
                Debug.LogWarning("No place available on this seat");
                return;
            }

            this.charactersAssociatedToSeatIdx.Add(photonViewId, seatIdx);

            Debug.Log($"There is {this.charactersAssociatedToSeatIdx.Count} seats used for {this.name}");

            // Sit if it's my character
            if (RoomManager.LocalCharacter.photonView.ViewID == photonViewId) {
                RoomManager.LocalCharacter.Sit(this, this.seatPositions[seatIdx]);
            }
        }

        /**
         * Retrieve the nearest seat idx from the character
         */
        private int GetAvailableSeatIdx() {
            var seatFound = seatPositions.Where((t, i) => !this.charactersAssociatedToSeatIdx.ContainsValue(i))
                .OrderBy(t => Vector3.Distance(t.position, RoomManager.LocalCharacter.transform.position))
                .First();

            return seatFound ? Array.IndexOf(seatPositions, seatFound) : -1;
        }

        public void RevokeSeat(Character character) {
            photonView.RPC("RPC_RevokeSeat", RpcTarget.All, character.photonView.ViewID);
        }

        [PunRPC]
        public void RPC_RevokeSeat(int photonViewId) {
            if (!this.charactersAssociatedToSeatIdx.ContainsKey(photonViewId)) return;

            this.charactersAssociatedToSeatIdx.Remove(photonViewId);
        }
    }
}