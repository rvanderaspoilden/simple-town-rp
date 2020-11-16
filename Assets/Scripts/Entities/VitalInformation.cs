using System;
using UnityEngine;

namespace Sim.Entities {
    [Serializable]
    public class VitalInformation {
        [SerializeField]
        private float thirst;

        [SerializeField]
        private float hungry;

        [SerializeField]
        private float sleep;

        public void SetSleep(float value) {
            this.sleep = value;
        }

        public float GetSleep() {
            return this.sleep;
        }

        public void SetHungry(float value) {
            this.hungry = value;
        }

        public float GetHungry() {
            return this.hungry;
        }

        public void SetThirst(float value) {
            this.thirst = value;
        }

        public float GetThirst() {
            return this.thirst;
        }
    }
}