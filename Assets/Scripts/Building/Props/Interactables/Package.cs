using System.Collections;
using System.Collections.Generic;
using Sim.Building;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Interactables {
    public class Package : Interactable {
        [Header("Package Settings")]
        [SerializeField] private Action useAction;

        [Header("Package debug")]
        [SerializeField] private PropsConfig propsInside;
        protected override void SetupActions() {
            this.actions = new Action[1] {useAction};
        }

        public override void Use() {
            // todo s'asseoir
        }

        public PropsConfig GetPropsInside() {
            return this.propsInside;
        }

        public void SetPropsInside(PropsConfig props) {
            this.propsInside = props;
        }

        public void SetPropsInside(string pathToConfig) {
            // todo
        }
    }
}