using System;
using System.Collections.Generic;
using Sim.Building;
using Sim.Enums;
using UnityEngine;

namespace Sim.Interactables {
    public class Interactable : Props {
        [Header("Basic settings")]
        [SerializeField] protected float rangeToInteract; // Todo create scriptable object

        [Header("Basic settings debug")]
        [SerializeField] protected Action[] actions;
        
        private void Start() {
            this.SetupActions();
        }

        protected virtual void SetupActions() {
            throw new NotImplementedException();
        }

        public virtual bool CanInteract(Vector3 target) {
            return Mathf.Abs(Vector3.Distance(this.transform.position, target)) <= this.rangeToInteract;
        }

        public virtual Action[] GetActions() {
            return this.actions;
        }

        public void DoAction(Action action) {
            Debug.Log("do action : " + action);

            switch (action.GetActionType()) {
                case ActionTypeEnum.USE:
                    this.Use();
                    break;
                case ActionTypeEnum.MOVE:
                    this.Move();
                    break;

                case ActionTypeEnum.BUILD:
                    this.Build();
                    break;
            }
        }

        public virtual void Use() {
            throw new NotImplementedException();
        }

        public virtual void Build() {
            throw new NotImplementedException();
        }

        public virtual void Move() {
            throw new NotImplementedException();
        }
    }
}