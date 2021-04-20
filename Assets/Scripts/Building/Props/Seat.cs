using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
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

        private readonly SyncDictionary<int, uint> charactersAssociatedToSeatIdx = new SyncDictionary<int, uint>();

        private readonly SyncDictionary<int, uint> charactersAssociatedToCouchIdx = new SyncDictionary<int, uint>();

        protected override void Execute(Action action) {
            if (action.Type.Equals(ActionTypeEnum.SIT)) {
                int seatIdx = GetAvailableSeatIdx();

                if (seatIdx != -1) CmdAssignSeat(seatIdx);
            } else if (action.Type.Equals(ActionTypeEnum.COUCH)) {
                int couchIdx = GetAvailableCouchIdx();

                if (couchIdx != -1) CmdAssignCouch(couchIdx);
            }
        }

        public override Action[] GetActions(bool withPriority = false) {
            Action[] actions = base.GetActions(withPriority);

            return actions.Where(x => {
                if (x.Type.Equals(ActionTypeEnum.SIT)) {
                    return this.GetKeyFromValue(this.charactersAssociatedToSeatIdx, PlayerController.Local.netId) == -1 && GetAvailableSeatIdx() != -1;
                }

                if (x.Type.Equals(ActionTypeEnum.COUCH)) {
                    return this.GetKeyFromValue(this.charactersAssociatedToCouchIdx, PlayerController.Local.netId) == -1 && GetAvailableCouchIdx() != -1;
                }

                if (x.Type.Equals(ActionTypeEnum.SELL) || x.Type.Equals(ActionTypeEnum.MOVE)) {
                    return charactersAssociatedToSeatIdx.Count == 0 && charactersAssociatedToCouchIdx.Count == 0;
                }

                return true;
            }).ToArray();
        }

        #region SIT MANAGEMENT

        /**
         * Retrieve the nearest seat idx from the character
         */
        private int GetAvailableSeatIdx() {
            var seatFound = seatPositions.Where((t, i) => !this.charactersAssociatedToSeatIdx.ContainsKey(i))
                .OrderBy(t => Vector3.Distance(t.position, PlayerController.Local.transform.position))
                .FirstOrDefault();

            return seatFound ? Array.IndexOf(seatPositions, seatFound) : -1;
        }

        [Command(requiresAuthority = false)]
        public void CmdAssignSeat(int seatIdx, NetworkConnectionToClient sender = null) {
            if (this.charactersAssociatedToSeatIdx.Count == seatPositions.Length) {
                Debug.LogWarning("No place available on this seat");
                return;
            }

            if (sender == null) {
                Debug.Log("Cannot retrieve identity");
                return;
            }

            this.charactersAssociatedToSeatIdx.Add(seatIdx, sender.identity.netId);

            TargetSit(sender.identity.connectionToClient, seatIdx);
        }

        [TargetRpc]
        public void TargetSit(NetworkConnection target, int seatIdx) {
            PlayerController.Local.Sit(this, this.seatPositions[seatIdx]);
        }

        public void RevokeSeat() {
            CmdRevokeSeat();
        }

        [Command(requiresAuthority = false)]
        public void CmdRevokeSeat(NetworkConnectionToClient sender = null) {
            if (sender == null) return;

            int seatIdx = GetKeyFromValue(this.charactersAssociatedToSeatIdx, sender.identity.netId);

            if (seatIdx != -1) this.charactersAssociatedToSeatIdx.Remove(seatIdx);
        }

        private int GetKeyFromValue(SyncDictionary<int, uint> syncDictionary, uint value) {
            foreach (KeyValuePair<int, uint> keyValuePair in syncDictionary) {
                if (keyValuePair.Value == value) {
                    return keyValuePair.Key;
                }
            }

            return -1;
        }

        #endregion

        /**
         * Retrieve the nearest seat idx from the character
         */
        private int GetAvailableCouchIdx() {
            var couchFound = couchPositions.Where((t, i) => !this.charactersAssociatedToCouchIdx.ContainsKey(i))
                .OrderBy(t => Vector3.Distance(t.position, PlayerController.Local.transform.position))
                .FirstOrDefault();

            return couchFound ? Array.IndexOf(couchPositions, couchFound) : -1;
        }

        [Command(requiresAuthority = false)]
        public void CmdAssignCouch(int couchIdx, NetworkConnectionToClient sender = null) {
            if (this.charactersAssociatedToCouchIdx.Count == couchPositions.Length) {
                Debug.LogWarning("No couch place available");
                return;
            }

            if (sender == null) {
                Debug.Log("Cannot retrieve identity");
                return;
            }

            this.charactersAssociatedToCouchIdx.Add(couchIdx, sender.identity.netId);

            TargetSleep(sender.identity.connectionToClient, couchIdx);
        }

        [TargetRpc]
        public void TargetSleep(NetworkConnection target, int couchIdx) {
            PlayerController.Local.Sleep(this, this.couchPositions[couchIdx]);
        }

        public void RevokeCouch() {
            CmdRevokeCouch();
        }

        [Command(requiresAuthority = false)]
        public void CmdRevokeCouch(NetworkConnectionToClient sender = null) {
            if (sender == null) return;

            int couchIdx = GetKeyFromValue(this.charactersAssociatedToCouchIdx, sender.identity.netId);

            if (couchIdx != -1) this.charactersAssociatedToCouchIdx.Remove(couchIdx);
        }
    }
}