using System;

namespace AI {
    public class Transition {
        public IState ToState { get; }

        public Func<bool> Condition { get; }

        public Transition(IState toState, Func<bool> predicate) {
            this.ToState = toState;
            this.Condition = predicate;
        }
    }
}