using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Building {
    public class Seat : Props {
        [Header("Settings")]
        [SerializeField]
        private Transform[] seatPositions;

        [SerializeField]
        private Transform[] couchPositions;

        private Dictionary<int, int> charactersAssociatedToSeatIdx;
        
        private Dictionary<int, int> charactersAssociatedToCouchIdx;

        protected override void Awake() {
            base.Awake();

            this.charactersAssociatedToSeatIdx = new Dictionary<int, int>();
            this.charactersAssociatedToCouchIdx = new Dictionary<int, int>();
        }

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.SIT)) {
                int seatIdx = GetAvailableSeatIdx();

                if (seatIdx != -1) {
                    //photonView.RPC("RPC_AssignSeat", RpcTarget.All, RoomManager.LocalCharacter.photonView.ViewID, seatIdx);
                }
            } else if (action.Type.Equals(ActionTypeEnum.COUCH)) {
                int couchIdx = GetAvailableCouchIdx();

                if (couchIdx != -1) {
                    //photonView.RPC("RPC_AssignCouch", RpcTarget.All, RoomManager.LocalCharacter.photonView.ViewID, couchIdx);
                }
            }
        }

        public override Action[] GetActions(bool withPriority = false) {
            Action[] actions = base.GetActions(withPriority);

            return actions.Where(x => {
                if (x.Type.Equals(ActionTypeEnum.SIT)) {
                    //return !this.charactersAssociatedToSeatIdx.ContainsKey(RoomManager.LocalCharacter.photonView.ViewID) && GetAvailableSeatIdx() != -1;
                }
                
                if (x.Type.Equals(ActionTypeEnum.COUCH)) {
                    //return !this.charactersAssociatedToCouchIdx.ContainsKey(RoomManager.LocalCharacter.photonView.ViewID) && GetAvailableCouchIdx() != -1;
                }
                
                if (x.Type.Equals(ActionTypeEnum.SELL) || x.Type.Equals(ActionTypeEnum.MOVE) ) {
                    return charactersAssociatedToSeatIdx.Count == 0 && charactersAssociatedToCouchIdx.Count == 0;
                }

                return true;
            }).ToArray();
        }

        public override void Synchronize(Player playerTarget) {
            base.Synchronize(playerTarget);

            if (this.charactersAssociatedToSeatIdx.Count > 0 || this.charactersAssociatedToCouchIdx.Count > 0) {
                photonView.RPC("RPC_Setup", playerTarget, this.charactersAssociatedToSeatIdx, this.charactersAssociatedToCouchIdx);
            }
        }

        [PunRPC]
        public void RPC_Setup(Dictionary<int, int> associatedSeats, Dictionary<int, int> associatedCouches) {
            this.charactersAssociatedToSeatIdx = associatedSeats;
            this.charactersAssociatedToCouchIdx = associatedCouches;
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
            /*if (RoomManager.LocalCharacter.photonView.ViewID == photonViewId) {
                RoomManager.LocalCharacter.Sit(this, this.seatPositions[seatIdx]);
            }*/
        }
        
        [PunRPC]
        public void RPC_AssignCouch(int photonViewId, int couchIdx) {
            if (this.charactersAssociatedToCouchIdx.Count == couchPositions.Length) {
                Debug.LogWarning("No couch place available");
                return;
            }

            this.charactersAssociatedToCouchIdx.Add(photonViewId, couchIdx);

            Debug.Log($"There is {this.charactersAssociatedToCouchIdx.Count} couch positions used for {this.name}");

            // Sit if it's my character
            /*if (RoomManager.LocalCharacter.photonView.ViewID == photonViewId) {
                RoomManager.LocalCharacter.Sleep(this, this.couchPositions[couchIdx]);
            }*/
        }

        /**
         * Retrieve the nearest seat idx from the character
         */
        private int GetAvailableSeatIdx() {
            var seatFound = seatPositions.Where((t, i) => !this.charactersAssociatedToSeatIdx.ContainsValue(i))
                .OrderBy(t => Vector3.Distance(t.position, RoomManager.LocalPlayer.transform.position))
                .FirstOrDefault();

            return seatFound ? Array.IndexOf(seatPositions, seatFound) : -1;
        }
        
        /**
         * Retrieve the nearest seat idx from the character
         */
        private int GetAvailableCouchIdx() {
            var couchFound = couchPositions.Where((t, i) => !this.charactersAssociatedToCouchIdx.ContainsValue(i))
                .OrderBy(t => Vector3.Distance(t.position, RoomManager.LocalPlayer.transform.position))
                .FirstOrDefault();

            return couchFound ? Array.IndexOf(couchPositions, couchFound) : -1;
        }

        public void RevokeSeat(PlayerController player) {
            //photonView.RPC("RPC_RevokeSeat", RpcTarget.All, character.photonView.ViewID);
        }
        
        public void RevokeCouch(PlayerController player) {
            //photonView.RPC("RPC_RevokeCouch", RpcTarget.All, character.photonView.ViewID);
        }

        [PunRPC]
        public void RPC_RevokeSeat(int photonViewId) {
            if (!this.charactersAssociatedToSeatIdx.ContainsKey(photonViewId)) return;

            this.charactersAssociatedToSeatIdx.Remove(photonViewId);
        }
        
        [PunRPC]
        public void RPC_RevokeCouch(int photonViewId) {
            if (!this.charactersAssociatedToCouchIdx.ContainsKey(photonViewId)) return;

            this.charactersAssociatedToCouchIdx.Remove(photonViewId);
        }
    }
}