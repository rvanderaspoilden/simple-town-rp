using System;
using System.Collections.Generic;
using UnityEngine;

namespace AI {
    public class StateMachine {
        private IState currentState;

        private Dictionary<Type, List<Transition>> transitions = new Dictionary<Type, List<Transition>>();
        private List<Transition> currentTransitions;

        private List<Transition>
            anyTransitions = new List<Transition>(); // Represents all transitions which can be triggered whenever

        private static readonly List<Transition> EmptyTransitions = new List<Transition>(0);

        public void Tick() {
            Transition transition = this.GetTransition();
            if (transition != null) this.SetState(transition.ToState);
            this.currentState?.Tick();
            //Debug.Log("Current state : " + this.GetCurrentState());
        }

        public IState GetCurrentState() {
            return this.currentState;
        }

        public void SetState(IState state) {
            if (this.currentState == state) return;

            this.currentState?.OnExit();

            this.currentState = state;

            this.transitions.TryGetValue(this.currentState.GetType(), out this.currentTransitions);

            this.currentTransitions ??= EmptyTransitions;

            this.currentState.OnEnter();
        }

        public void AddTransition(IState from, IState to, Func<bool> predicate) {
            if (!this.transitions.TryGetValue(from.GetType(), out List<Transition> linkedTransitions)) {
                linkedTransitions = new List<Transition>();
                this.transitions[from.GetType()] = linkedTransitions;
            }

            linkedTransitions.Add(new Transition(to, predicate));
        }

        public void AddAnyTransition(IState to, Func<bool> predicate) {
            this.anyTransitions.Add(new Transition(to, predicate));
        }

        private Transition GetTransition() {
            foreach (Transition transition in this.anyTransitions) {
                if (transition.Condition()) return transition;
            }

            foreach (Transition transition in this.currentTransitions) {
                if (transition.Condition()) return transition;
            }

            return null;
        }
    }
}