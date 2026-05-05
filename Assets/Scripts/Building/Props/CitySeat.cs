using System.Linq;
using Interaction;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Building {
    /// <summary>
    /// Seat/couch prop for the City scene without a NetworkIdentity.
    /// State (who is sitting) is owned by CityPropsStateManager.
    /// Assign a unique propId in the Inspector for each city seat instance.
    /// </summary>
    public class CitySeat : StaticProp, ISeatBehavior {
        [Header("Seat Settings")]
        [SerializeField] private int propId;
        [SerializeField] private Transform[] seatPositions;
        [SerializeField] private Transform[] couchPositions;

        protected override void Start() {
            base.Start();
            CityPropsStateManager.Instance?.RegisterSeat(propId, this);
        }

        protected override void Execute(Action action) {
            if (action.Type == ActionTypeEnum.SIT) {
                int seatIdx = GetAvailableSeatIdx();
                if (seatIdx != -1) CityPropsStateManager.Instance?.CmdRequestSit(propId, seatIdx);
            } else if (action.Type == ActionTypeEnum.COUCH) {
                int couchIdx = GetAvailableCouchIdx();
                if (couchIdx != -1) CityPropsStateManager.Instance?.CmdRequestCouch(propId, couchIdx);
            }
        }

        public override Action[] GetActions(bool withPriority = false) {
            Action[] result = base.GetActions(withPriority);
            if (CityPropsStateManager.Instance == null || PlayerController.Local == null) return result;

            uint localNetId = PlayerController.Local.netId;
            return result.Where(x => {
                if (x.Type == ActionTypeEnum.SIT) {
                    return !CityPropsStateManager.Instance.IsPlayerInSeat(propId, localNetId)
                           && GetAvailableSeatIdx() != -1;
                }
                if (x.Type == ActionTypeEnum.COUCH) {
                    return !CityPropsStateManager.Instance.IsPlayerOnCouch(propId, localNetId)
                           && GetAvailableCouchIdx() != -1;
                }
                return true;
            }).ToArray();
        }

        // Called by CityPropsStateManager.TargetConfirmSit/TargetConfirmCouch
        public Transform GetSeatTransform(int seatIdx) => seatPositions[seatIdx];
        public Transform GetCouchTransform(int couchIdx) => couchPositions[couchIdx];

        // ISeatBehavior — called by CharacterSit/CharacterSleep.OnExit()
        public void RevokeSeat() => CityPropsStateManager.Instance?.CmdRevokeSeat(propId);
        public void RevokeCouch() => CityPropsStateManager.Instance?.CmdRevokeCouch(propId);

        private int GetAvailableSeatIdx() {
            if (seatPositions == null || CityPropsStateManager.Instance == null) return -1;
            for (int i = 0; i < seatPositions.Length; i++) {
                if (!CityPropsStateManager.Instance.IsSeatOccupied(propId, i)) return i;
            }
            return -1;
        }

        private int GetAvailableCouchIdx() {
            if (couchPositions == null || CityPropsStateManager.Instance == null) return -1;
            for (int i = 0; i < couchPositions.Length; i++) {
                if (!CityPropsStateManager.Instance.IsCouchOccupied(propId, i)) return i;
            }
            return -1;
        }
    }
}
